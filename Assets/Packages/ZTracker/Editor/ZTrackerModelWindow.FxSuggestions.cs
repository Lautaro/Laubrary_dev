using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // Editor view of the literal commands accepted by TrackerCommandCompiler. Keep status and ranges
    // aligned with Documentation~/COMMANDS.md when the engine's supported subset changes.
    internal static class FxCommandCatalog
    {
        internal sealed class Entry
        {
            internal readonly string id,notation,description,range;
            internal readonly bool local,track,volume,pan;
            internal readonly CommandKind kind;
            internal string Display=>notation+" — "+description+" ("+range+")";
            internal Entry(string id,string notation,string description,string range,bool local=false,bool track=false,bool volume=false,bool pan=false,CommandKind kind=CommandKind.None)
            {this.id=id;this.notation=notation;this.description=description;this.range=range;this.local=local;this.track=track;this.volume=volume;this.pan=pan;this.kind=kind==CommandKind.None?CommandColors.KindOf(id):kind;}
        }
        static readonly Entry[] entries={
            new Entry("0A","0Axy","Cycle pitch: base, +x, +y semitones on successive ticks", "x/y=0–F",true,true),
            new Entry("0U","0Uxx","Raise pitch by xx/16 semitones per row", "hex 00–FF; 00 repeats",true,true),
            new Entry("0D","0Dxx","Lower pitch by xx/16 semitones per row", "hex 00–FF; 00 repeats",true,true),
            new Entry("0G","0Gxx","Glide toward the row's note at xx/16 semitones per row", "hex 00–FF; FF snaps",true,true),
            new Entry("0V","0Vxy","Vibrato: x is rate, y is pitch depth", "x/y=0–F",true,true),
            new Entry("0I","0Ixx","Raise sample gain by xx/256 per row", "hex 00–FF; 00 repeats",true,true),
            new Entry("0O","0Oxx","Lower sample gain by xx/256 per row", "hex 00–FF; 00 repeats",true,true),
            new Entry("0T","0Txy","Tremolo: x is rate, y is gain depth", "x/y=0–F",true,true),
            new Entry("0C","0Cxy","At tick y set sample gain to x/15", "x/y=0–F",true,true),
            new Entry("0S","0Sxx","Start sample at fractional offset or numbered slice xx", "hex 00–FF",true,true),
            new Entry("0B","0Bxx","Sample direction", "00 reverse, 01 forward",true,true),
            new Entry("0E","0Exx","Seek active sample envelopes to xx/256 of their length", "hex 00–FF",true,true),
            new Entry("0N","0Nxy","Auto pan: x is rate, y is stereo depth", "x/y=0–F",true,true),
            new Entry("0Y","0Yxx","Play row note with xx/255 chance; 00 is exclusive mode", "hex 00–FF",true,true),
            new Entry("0M","0Mxx","Set instrument gain from -60 to +3 dB", "hex 00–FF",track:true),
            new Entry("0Q","0Qxx","Delay note launch by xx ticks", "00–ticks per line",track:true),
            new Entry("0R","0Rxy","Retrigger every y ticks; x changes retrigger gain", "x=0–F, y=1–F",track:true),
            new Entry("0L","0Lxx","Set track pre gain from silence to +3 dB", "hex 00–FF",track:true),
            new Entry("0P","0Pxx","Set track pre pan: 00 left, 80 center, FF right", "hex 00–FF",track:true),
            new Entry("0X","0X00","Stop track notes and effects", "00 only",track:true),
            new Entry("0J","0Jxx","Route output to Master or an existing parent group", "00 Master; FF downward parents",track:true),
            new Entry("ZT","ZTxx","Set song tempo to xx BPM", "00 stop; 20–FF BPM",track:true),
            new Entry("ZL","ZLxx","Set lines per beat to xx", "00 stop; 01–FF",track:true),
            new Entry("ZK","ZKxx","Set ticks per line to xx", "01–10",track:true),
            new Entry("ZG","ZG00","Turn groove off", "00 only",track:true),
            new Entry("ZB","ZBxx","Continue next pattern at row xx after this row", "hex 00–FF",track:true),
            new Entry("ZD","ZDxx","Hold current row for xx extra row durations", "hex 00–FF",track:true),
            new Entry("I","Ix","Slide sample gain up", "x=0–F; 0 repeats",volume:true),
            new Entry("O","Ox","Slide sample gain down", "x=0–F; 0 repeats",volume:true),
            new Entry("J","Jx","Slide pan left", "x=0–F",pan:true),
            new Entry("K","Kx","Slide pan right", "x=0–F",pan:true),
            new Entry("U","Ux","Slide pitch up", "x=0–F; 0 repeats",volume:true,pan:true),
            new Entry("D","Dx","Slide pitch down", "x=0–F; 0 repeats",volume:true,pan:true),
            new Entry("G","Gx","Glide toward note", "x=0–F; F snaps",volume:true,pan:true),
            new Entry("C","Cx","Cut note at tick", "x=0–F",volume:true,pan:true),
            new Entry("B","B0/B1","Sample direction", "0 reverse, 1 forward",volume:true,pan:true),
            new Entry("Q","Qx","Delay note by ticks", "x=0–F",volume:true,pan:true),
            new Entry("Y","Yx","Note probability", "x=0–F",volume:true,pan:true),
            new Entry("R","Rx","Retrigger note", "x=1–F",volume:true,pan:true)
        };
        static char Digit(int ordinal)=>"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[ordinal];
        static ZTrackerModelWindow.FieldKind IdKind(ZTrackerModelWindow.FieldKind kind)=>kind==ZTrackerModelWindow.FieldKind.EffectValue?ZTrackerModelWindow.FieldKind.EffectId:kind==ZTrackerModelWindow.FieldKind.LocalValue?ZTrackerModelWindow.FieldKind.LocalId:kind;
        static bool Applies(Entry e,ZTrackerModelWindow.FieldKind kind)=>kind==ZTrackerModelWindow.FieldKind.LocalId?e.local:kind==ZTrackerModelWindow.FieldKind.EffectId?e.track:kind==ZTrackerModelWindow.FieldKind.Volume?e.volume:kind==ZTrackerModelWindow.FieldKind.Pan&&e.pan;
        static bool Matches(Entry e,string id)=>e.id==id||e.id.Length==1&&id.Length==2&&id[0]==e.id[0]&&!(e.id=="B"&&id[1]>'1')&&!(e.id=="R"&&id[1]=='0');
        /// The built-in commands a field takes, in catalogue order.
        internal static IEnumerable<Entry> Builtins(ZTrackerModelWindow.FieldKind kind){kind=IdKind(kind);foreach(var e in entries)if(Applies(e,kind))yield return e;}
        /// The built-in command an id typed in a field means, or null. Allocation-free: the grid calls it for every cell it draws.
        internal static Entry Builtin(string id,ZTrackerModelWindow.FieldKind kind)
        {
            if(string.IsNullOrEmpty(id))return null;kind=IdKind(kind);
            for(int i=0;i<entries.Length;i++){var e=entries[i];if(Applies(e,kind)&&Matches(e,id))return e;}
            return null;
        }
        /// Whether an id addresses a parameter (or the bypass) of an imported device declared on the track.
        internal static bool IsDeviceId(string id,TrackData owner)
        {
            if(id==null||id.Length!=2||owner?.sourceDevices==null)return false;int first="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(id[0]);if(first<1||first>34)return false;
            foreach(var s in owner.sourceDevices)if(s!=null&&s.ordinal==first)return true;return false;
        }
        internal static Entry SongCommand(DynamicParameterData p)=>new Entry(ZTrackerCommandRegistry.Normalize(p.id),ZTrackerCommandRegistry.Normalize(p.id)+"xx",p.name+" ("+p.scope.ToString().ToLowerInvariant()+" command)","00 = "+p.min.ToString("0.##")+", FF = "+p.max.ToString("0.##"),track:true);
        internal static IEnumerable<Entry> Devices(TrackData owner)
        {
            if(owner?.sourceDevices==null)yield break;
            foreach(var source in owner.sourceDevices.Where(s=>s!=null&&s.ordinal>=1&&s.ordinal<=34))
            {
                string first=Digit(source.ordinal).ToString();string name=string.IsNullOrWhiteSpace(source.pluginId)?"declared device":source.pluginId;
                yield return new Entry(first+"0",first+"0xx","Bypass "+name,"00 off; 01 on",track:true,kind:CommandKind.Device);
                if(source.parameters==null)continue;
                foreach(var parameter in source.parameters.Where(p=>p!=null&&p.ordinal>=1&&p.ordinal<=35))
                {
                    string id=first+Digit(parameter.ordinal);string label=string.IsNullOrWhiteSpace(parameter.parameter)?"parameter "+parameter.ordinal:parameter.parameter;
                    yield return new Entry(id,id+"xx","Set "+name+" / "+label,"hex 00–FF",track:true,kind:CommandKind.Device);
                }
            }
        }
        internal static IEnumerable<Entry> For(ZTrackerModelWindow.FieldKind kind,TrackData owner=null,SongData song=null)
        {
            kind=IdKind(kind);
            foreach(var entry in Builtins(kind))yield return entry;
            if(kind!=ZTrackerModelWindow.FieldKind.EffectId)yield break;
            // Every song command, in or out of context: this answers "what does this id mean", not "what belongs here".
            if(song?.dynamicParameters!=null)foreach(var p in song.dynamicParameters.Where(p=>p!=null&&ZTrackerCommandRegistry.ValidateId(null,p.id,null,out _)==null).OrderBy(p=>p.id,StringComparer.Ordinal))
                yield return SongCommand(p);
            foreach(var entry in Devices(owner))yield return entry;
        }
        internal static Entry Find(string id,ZTrackerModelWindow.FieldKind kind,TrackData owner=null,SongData song=null)=>string.IsNullOrEmpty(id)?null:For(kind,owner,song).FirstOrDefault(e=>Matches(e,id));
        internal static bool SupportsValue(string id,int value)
        {
            if(id=="0B")return value<=1;
            if(id=="0X"||id=="ZG")return value==0;
            if(id=="0R")return (value&15)!=0;
            if(id=="ZK")return value>=1&&value<=16;
            if(id=="ZT")return value==0||value>=32;
            return value>=0&&value<=255;
        }
    }

    // The command suggestion popover (T-0570, made context-aware in T-0599). The first character typed in an FX or
    // command field opens it; it lists only the commands that belong on that row of that track (the shared context
    // rule in ZTrackerCommandRegistry.InContext) plus the built-ins, and narrows by id and by friendly name as more is
    // typed. The grid keeps keyboard focus throughout. The list is built once when it opens; typing only re-ranks an
    // index array and rebinds a fixed pool of rows, so it stays fast with hundreds of commands.
    public sealed partial class ZTrackerModelWindow
    {
        internal sealed class Suggestion
        {
            internal string id,name,scope,tooltip,spaced;internal CommandLook look;
        }
        const int SuggestionRows=8;
        internal ZuiPopover fxSuggestions;
        internal readonly List<Suggestion> suggestions=new List<Suggestion>();
        internal int[] suggestionOrder=new int[0];
        internal int suggestionCount,suggestionHighlight,suggestionTop;
        internal bool suggestionTypedRow;   // the list's first row enters exactly what was typed
        FieldSlot suggestionSlot;int suggestionRow=-1,suggestionSub=-1;
        Label suggestionQuery,suggestionTally,suggestionMore;
        readonly VisualElement[] suggestionRowViews=new VisualElement[SuggestionRows];
        readonly Label[] suggestionChips=new Label[SuggestionRows],suggestionNames=new Label[SuggestionRows],suggestionScopes=new Label[SuggestionRows];

        void CloseFxSuggestions(){fxSuggestions?.Close();fxSuggestions=null;suggestionRow=-1;}

        /// The commands that belong in a field at a row: song commands in context (instrument, effect, track, song), then
        /// the track's imported devices, then the built-ins this field takes.
        List<Suggestion> SuggestionsFor(FieldSlot slot,int line)
        {
            var list=new List<Suggestion>();var t=Data.tracks[slot.track];
            Suggestion Make(string id,string name,string scope,string tooltip,CommandLook look){var s=new Suggestion{id=id,name=name,scope=scope,tooltip=tooltip,look=look};s.spaced=" "+Fold(name);return s;}
            if(slot.kind==FieldKind.EffectId||slot.kind==FieldKind.EffectValue)
            {
                RefreshLookDefinitions();
                var inContext=ZTrackerCommandRegistry.InContext(Data,Pattern?.id,t.id,line,order).Where(p=>ZTrackerCommandRegistry.ValidateId(null,p.id,null,out _)==null).OrderBy(p=>ZTrackerCommandRegistry.Normalize(p.id),StringComparer.Ordinal);
                foreach(var p in inContext){string id=ZTrackerCommandRegistry.Normalize(p.id);list.Add(Make(id,p.name,p.scope.ToString(),id+" — "+p.name+": "+CommandOwner(p)+" (00 = "+p.min.ToString("0.##")+", FF = "+p.max.ToString("0.##")+").",new CommandLook(true,true,true,CommandKind.None,ZTrackerCommandRegistry.ColorOf(p),p)));}
                foreach(var e in FxCommandCatalog.Devices(t))list.Add(Make(e.id,e.description,"Device",e.Display+".",new CommandLook(true,true,false,CommandKind.Device,CommandColors.KindColor(CommandKind.Device),null)));
            }
            foreach(var e in FxCommandCatalog.Builtins(slot.kind))list.Add(Make(e.id,e.description,"Built-in",e.Display+". "+CommandColors.KindName(e.kind)+" command.",new CommandLook(true,true,false,e.kind,CommandColors.KindColor(e.kind),null)));
            return list;
        }

        /// What a song command sets, in words: "Bass / volume", "Lead / Post gain", "Lead / Gain 1 setting 2", "song tempo".
        string CommandOwner(DynamicParameterData p)
        {
            var target=p.target;switch(p.scope)
            {
                case DynamicScope.Song:return "sets the song's "+(target.parameter=="bpm"?"tempo":target.parameter);
                case DynamicScope.Track:{var t=Data.tracks.Find(x=>x!=null&&x.id==target.trackId);int at=Array.IndexOf(ZTrackerCommandRegistry.TrackParameters,target.parameter);return "sets "+(t?.name??"a removed track")+" / "+(at>=0?TrackParameterLabels[at]:target.parameter);}
                case DynamicScope.Effect:{var t=Data.tracks.Find(x=>x!=null&&x.id==target.trackId);var node=Records(t?.devices?.nodes).FirstOrDefault(n=>n.uid==target.deviceId);var desc=node==null?null:ZoundEffectDescriptors.Get(node.type);
                    return "sets "+(t?.name??"a removed track")+" / "+(desc==null?"a removed effect":desc.displayName+(target.index>=0&&target.index<desc.parameters.Length?" "+desc.parameters[target.index].name:""));}
                default:{var asset=Data.instruments.FirstOrDefault(i=>i?.model!=null&&i.model.id==target.instrumentId);return "sets "+(asset==null?"a removed instrument":InstrumentTitle(asset))+" / "+(target.kind==ParameterKind.InstrumentMacro?"Macro "+(target.index+1):FieldLabel(target.parameter??""));}
            }
        }

        static string Fold(string text)
        {
            var b=new System.Text.StringBuilder(text?.Length??0);bool space=false;
            foreach(char c in text??""){if(char.IsLetterOrDigit(c)){b.Append(char.ToUpperInvariant(c));space=false;}else if(!space){b.Append(' ');space=true;}}
            return b.ToString();
        }

        /// Ranks the open list against the typed text: ids that start with it, then names with a word that starts with
        /// it, then (from three characters on) names with a word that contains it. Writes into a reused index array; nothing is allocated.
        internal void FilterSuggestions(string query)
        {
            int n=suggestions.Count;if(suggestionOrder.Length<n)suggestionOrder=new int[n];suggestionCount=0;
            for(int tier=0;tier<3;tier++)
                for(int i=0;i<n;i++)
                {
                    var s=suggestions[i];bool idMatch=query.Length<=s.id.Length&&string.CompareOrdinal(s.id,0,query,0,query.Length)==0;
                    bool hit=tier==0?idMatch:!idMatch&&(tier==1?WordStart(s.spaced,query):query.Length>=3&&!WordStart(s.spaced,query)&&s.spaced.IndexOf(query,StringComparison.Ordinal)>=0);
                    if(hit)suggestionOrder[suggestionCount++]=i;
                }
        }
        static bool WordStart(string spaced,string query)
        {
            for(int at=spaced.IndexOf(query,StringComparison.Ordinal);at>=0;at=spaced.IndexOf(query,at+1,StringComparison.Ordinal))if(at>0&&spaced[at-1]==' ')return true;
            return false;
        }

        /// Opens the list for the cursor cell, or re-ranks it as more is typed.
        void ShowFxSuggestions(FieldSlot slot,string typed)
        {
            if(grid==null||!labels.TryGetValue(new Vector2Int(row,sub),out var anchor))return;
            if(fxSuggestions==null||!fxSuggestions.IsOpen||suggestionRow!=row||suggestionSub!=sub)
            {
                CloseFxSuggestions();suggestions.Clear();suggestions.AddRange(SuggestionsFor(slot,row));if(suggestions.Count==0)return;
                suggestionSlot=slot;suggestionRow=row;suggestionSub=sub;
                float width=Mathf.Clamp((rootVisualElement.layout.width>0?rootVisualElement.layout.width:400)-16,180,300);
                fxSuggestions=Z.Popover(anchor,panel=>{
                    panel.name="fx-suggestions";panel.AddToClassList("tracker-suggest");panel.style.width=width;
                    var head=new VisualElement();head.AddToClassList("tracker-suggest__head");panel.Add(head);
                    suggestionQuery=Z.Text("",tooltip:"What you have typed. Up/Down choose, Enter or Tab enters the highlighted command, Esc cancels, Backspace removes a character. Any two characters can be entered: a command that is not listed is entered as typed and flagged if it is not defined.");suggestionQuery.name="fx-suggestions-query";suggestionQuery.AddToClassList("tracker-suggest__query");head.Add(suggestionQuery);
                    suggestionTally=Z.Text("",tooltip:"How many of the commands that belong on this row match what you typed.");suggestionTally.name="fx-suggestions-tally";suggestionTally.AddToClassList("tracker-suggest__tally");head.Add(suggestionTally);
                    var rows=new VisualElement{name="fx-suggestions-rows"};panel.Add(rows);
                    rows.RegisterCallback<WheelEvent>(e=>{ScrollSuggestions(e.delta.y>0?1:-1);e.StopPropagation();});
                    for(int i=0;i<SuggestionRows;i++)
                    {
                        int slotIndex=i;var view=new VisualElement{name="fx-suggestion-"+i};view.AddToClassList("tracker-suggest__row");
                        var chip=new Label();chip.AddToClassList("tracker-suggest__chip");chip.pickingMode=PickingMode.Ignore;
                        var name=new Label();name.AddToClassList("tracker-suggest__name");name.pickingMode=PickingMode.Ignore;
                        var scope=new Label();scope.AddToClassList("tracker-suggest__scope");scope.pickingMode=PickingMode.Ignore;
                        view.Add(chip);view.Add(name);view.Add(scope);
                        view.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;e.StopPropagation();AcceptSuggestion(suggestionTop+slotIndex);});
                        view.RegisterCallback<PointerEnterEvent>(e=>{suggestionHighlight=suggestionTop+slotIndex;BindSuggestions();});
                        suggestionRowViews[i]=view;suggestionChips[i]=chip;suggestionNames[i]=name;suggestionScopes[i]=scope;rows.Add(view);
                    }
                    suggestionMore=Z.Text("",tooltip:"More commands match: keep typing to narrow the list, or scroll with Down or the mouse wheel.");suggestionMore.name="fx-suggestions-more";suggestionMore.AddToClassList("tracker-suggest__more");panel.Add(suggestionMore);
                },new ZuiPopover.Options{modal=false,gap=1});
            }
            FilterSuggestions(typed);
            suggestionTypedRow=typed.Length>=2&&!IsColumnKind(slot.kind);
            suggestionHighlight=suggestionTypedRow&&suggestionCount>0?1:0;suggestionTop=0;
            BindSuggestions();
        }
        static bool IsColumnKind(FieldKind kind)=>kind==FieldKind.Volume||kind==FieldKind.Pan;
        int SuggestionTotal=>suggestionCount+(suggestionTypedRow?1:0);

        void ScrollSuggestions(int by){int total=SuggestionTotal;suggestionTop=Mathf.Clamp(suggestionTop+by,0,Math.Max(0,total-SuggestionRows));suggestionHighlight=Mathf.Clamp(suggestionHighlight,suggestionTop,suggestionTop+SuggestionRows-1);BindSuggestions();}
        void MoveSuggestion(int by)
        {
            int total=SuggestionTotal;if(total==0)return;suggestionHighlight=Mathf.Clamp(suggestionHighlight+by,0,total-1);
            if(suggestionHighlight<suggestionTop)suggestionTop=suggestionHighlight;if(suggestionHighlight>=suggestionTop+SuggestionRows)suggestionTop=suggestionHighlight-SuggestionRows+1;BindSuggestions();
        }

        /// Rebinds the fixed row pool to the current ranking.
        void BindSuggestions()
        {
            if(fxSuggestions==null||!fxSuggestions.IsOpen||suggestionQuery==null)return;int total=SuggestionTotal;
            suggestionQuery.text=pendingId;suggestionTally.text=suggestionCount+" of "+suggestions.Count;
            for(int i=0;i<SuggestionRows;i++)
            {
                int at=suggestionTop+i;var view=suggestionRowViews[i];
                if(at>=total){view.style.display=DisplayStyle.None;continue;}
                view.style.display=DisplayStyle.Flex;view.EnableInClassList("tracker-suggest__row--hl",at==suggestionHighlight);
                if(suggestionTypedRow&&at==0)
                {
                    string typed=TypedId();var look=Look(suggestionSlot.kind,suggestionSlot.track,typed);
                    suggestionChips[i].text=typed;CommandColors.Paint(suggestionChips[i],look);suggestionNames[i].text=look.known?"Enter as typed":"Enter as typed: not defined";suggestionScopes[i].text=look.known?"":"Undefined";
                    view.tooltip=look.known?"Enter "+typed+" exactly as typed.":"Enter "+typed+" as typed. Nothing in this song is called "+typed+", so the cell is kept, flagged in red and does nothing until a setting is given that id (Song › Commands).";
                    continue;
                }
                var s=suggestions[suggestionOrder[at-(suggestionTypedRow?1:0)]];
                suggestionChips[i].text=s.id;CommandColors.Paint(suggestionChips[i],s.look);suggestionNames[i].text=s.name;suggestionScopes[i].text=s.scope;view.tooltip=s.tooltip;
            }
            int below=total-(suggestionTop+SuggestionRows);suggestionMore.text=below>0?"+"+below+" more":"";suggestionMore.style.display=below>0?DisplayStyle.Flex:DisplayStyle.None;
        }

        string TypedId()=>pendingId.Length>=2?pendingId.Substring(0,2):pendingId;

        /// Enters the suggestion at a list position (or what was typed, from the first row) into the cell it was opened for.
        internal void AcceptSuggestion(int at)
        {
            if(Pattern==null||suggestionSub<0||suggestionSub>=fields.Count)return;
            string id;int? initialValue=null;
            if(suggestionTypedRow&&at==0||SuggestionTotal==0)id=TypedId();
            else
            {
                int index=at-(suggestionTypedRow?1:0);if(index<0||index>=suggestionCount)return;
                // Column shorthands need their argument nibble; use zero as the documented neutral/default.
                var chosen=suggestions[suggestionOrder[index]];id=chosen.id.Length==1?chosen.id+(chosen.id=="R"?"1":"0"):chosen.id;
                if(chosen.id=="0R")initialValue=1;
            }
            if(id.Length<2)return;
            row=suggestionRow;sub=suggestionSub;var f=fields[sub];
            CloseFxSuggestions();SetCommandIdentifier(f,id,initialValue);Advance();grid?.Focus();
        }

        void CancelSuggestions(){CloseFxSuggestions();pendingId="";RefreshCells();}

        /// Keys that act on the open list (called before the pattern shortcuts, so Enter, Tab and Esc keep their usual
        /// meaning whenever the list is closed). Returns true when the key was used.
        bool SuggestionKey(KeyDownEvent e)
        {
            if(fxSuggestions==null||!fxSuggestions.IsOpen||pendingId=="")return false;
            switch(e.keyCode)
            {
                case KeyCode.UpArrow:MoveSuggestion(-1);return true;
                case KeyCode.DownArrow:MoveSuggestion(1);return true;
                case KeyCode.PageUp:MoveSuggestion(-SuggestionRows);return true;
                case KeyCode.PageDown:MoveSuggestion(SuggestionRows);return true;
                case KeyCode.Return:case KeyCode.KeypadEnter:case KeyCode.Tab:AcceptSuggestion(suggestionHighlight);return true;
                case KeyCode.Escape:CancelSuggestions();return true;
                case KeyCode.Backspace:pendingId=pendingId.Substring(0,pendingId.Length-1);if(pendingId=="")CancelSuggestions();else{ShowFxSuggestions(suggestionSlot,pendingId);ShowPendingInCell();}return true;
            }
            return false;
        }

        /// The cursor cell shows the first two typed characters while a command is being chosen.
        void ShowPendingInCell(){if(pendingId!=""&&labels.TryGetValue(new Vector2Int(row,sub),out var cell))cell.text=pendingId.Length>=2?pendingId.Substring(0,2):pendingId+"·";}

        /// One typed character in an id field: narrows the list, and enters the id once two characters name a command,
        /// or once nothing in the list matches what was typed (so typing is never blocked).
        bool TypeIdCharacter(FieldSlot f,char ch)
        {
            pendingId+=ch;ShowFxSuggestions(f,pendingId);ShowPendingInCell();
            if(pendingId.Length<2)return true;
            string typed=TypedId();bool open=fxSuggestions!=null&&fxSuggestions.IsOpen;
            if(IsColumnKind(f.kind)||pendingId.Length==2&&(KnownCommand(f,typed)||!open||suggestionCount==0)){CloseFxSuggestions();SetCommandIdentifier(f,typed);Advance();}
            return true;
        }
        bool KnownCommand(FieldSlot f,string id)=>FxCommandCatalog.Builtin(id,f.kind)!=null||(f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue)&&(ZTrackerCommandRegistry.Find(Data,id)!=null||FxCommandCatalog.IsDeviceId(id,Data.tracks[f.track]));

        void SetCommandIdentifier(FieldSlot f,string entered,int? initialValue=null)
        {
            bool id=f.kind==FieldKind.LocalId||f.kind==FieldKind.EffectId;
            SongEdit("enter command",()=>EditField(row,f,(n,x,v)=>{
                if(f.kind==FieldKind.EffectId)entered=entered.ToUpperInvariant();
                var c=new CommandData{present=true,identifier=entered,scope=f.kind==FieldKind.EffectId?(entered[0]=='Z'?CommandScope.Global:CommandScope.Track):CommandScope.Column};
                if(id){var before=f.kind==FieldKind.LocalId?n.sampleFx:x.command;if(before.identifier==entered){c.value=before.value;c.valuePresent=before.valuePresent;}if(initialValue.HasValue){c.value=initialValue.Value;c.valuePresent=true;}if(f.kind==FieldKind.LocalId)n.sampleFx=c;else x.command=c;}
                else{var value=f.kind==FieldKind.Volume?n.volume:n.pan;value.command=c;value.kind=ValueKind.Command;}
            }));
        }
    }
}
