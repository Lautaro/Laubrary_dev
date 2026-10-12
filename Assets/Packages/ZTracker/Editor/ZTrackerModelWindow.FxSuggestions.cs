using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
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
            internal string Display=>notation+" — "+description+" ("+range+")";
            internal Entry(string id,string notation,string description,string range,bool local=false,bool track=false,bool volume=false,bool pan=false)
            {this.id=id;this.notation=notation;this.description=description;this.range=range;this.local=local;this.track=track;this.volume=volume;this.pan=pan;}
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
        internal static IEnumerable<Entry> For(ZTrackerModelWindow.FieldKind kind,TrackData owner=null)
        {
            if(kind==ZTrackerModelWindow.FieldKind.EffectValue)kind=ZTrackerModelWindow.FieldKind.EffectId;
            if(kind==ZTrackerModelWindow.FieldKind.LocalValue)kind=ZTrackerModelWindow.FieldKind.LocalId;
            foreach(var entry in entries.Where(e=>kind==ZTrackerModelWindow.FieldKind.LocalId?e.local:kind==ZTrackerModelWindow.FieldKind.EffectId?e.track:kind==ZTrackerModelWindow.FieldKind.Volume?e.volume:kind==ZTrackerModelWindow.FieldKind.Pan?e.pan:false))yield return entry;
            if(kind!=ZTrackerModelWindow.FieldKind.EffectId||owner?.sourceDevices==null)yield break;
            foreach(var source in owner.sourceDevices.Where(s=>s!=null&&s.ordinal>=1&&s.ordinal<=34))
            {
                string first=Digit(source.ordinal).ToString();string name=string.IsNullOrWhiteSpace(source.pluginId)?"declared device":source.pluginId;
                yield return new Entry(first+"0",first+"0xx","Bypass "+name,"00 off; 01 on",track:true);
                if(source.parameters==null)continue;
                foreach(var parameter in source.parameters.Where(p=>p!=null&&p.ordinal>=1&&p.ordinal<=35))
                {
                    string id=first+Digit(parameter.ordinal);string label=string.IsNullOrWhiteSpace(parameter.parameter)?"parameter "+parameter.ordinal:parameter.parameter;
                    yield return new Entry(id,id+"xx","Set "+name+" / "+label,"hex 00–FF",track:true);
                }
            }
        }
        internal static Entry Find(string id,ZTrackerModelWindow.FieldKind kind,TrackData owner=null)=>string.IsNullOrEmpty(id)?null:For(kind,owner).FirstOrDefault(e=>e.id==id||e.id.Length==1&&id.Length==2&&id[0]==e.id[0]&&!(e.id=="B"&&id[1]>'1')&&!(e.id=="R"&&id[1]=='0'));
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

    public sealed partial class ZTrackerModelWindow
    {
        ZuiPopover fxSuggestions;
        void CloseFxSuggestions(){fxSuggestions?.Close();fxSuggestions=null;}

        void ShowFxSuggestions(FieldSlot slot,string prefix)
        {
            CloseFxSuggestions();if(grid==null||!labels.TryGetValue(new UnityEngine.Vector2Int(row,sub),out var anchor))return;
            var matches=FxCommandCatalog.For(slot.kind,Data.tracks[slot.track]).Where(e=>e.id.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length==0)return;
            int targetRow=row,targetSub=sub;
            fxSuggestions=Z.Popover(anchor,panel=>{
                panel.Add(Z.Text("FX commands",tooltip:"Choose a supported command for this column."));
                var results=new ScrollView();results.style.maxHeight=260;panel.Add(results);
                foreach(var entry in matches){var chosen=entry;results.Add(Z.Button(chosen.Display,"Enter "+chosen.notation+" in this pattern cell.",()=>{
                    if(Pattern==null||targetSub>=fields.Count)return;
                    row=targetRow;sub=targetSub;
                    // Column shorthands need their argument nibble; use zero as the documented neutral/default.
                    string id=chosen.id.Length==1?chosen.id+(chosen.id=="R"?"1":"0"):chosen.id;
                    int? initialValue=chosen.id=="0R" ? 1 : (int?)null;
                    CloseFxSuggestions();SetCommandIdentifier(fields[sub],id,initialValue);Advance();grid?.Focus();
                }));}
            },new ZuiPopover.Options{minWidth=250,maxWidth=410,modal=false});
        }

        void SetCommandIdentifier(FieldSlot f,string entered,int? initialValue=null)
        {
            bool id=f.kind==FieldKind.LocalId||f.kind==FieldKind.EffectId;
            SongEdit("enter command",()=>EditField(row,f,(n,x,v)=>{
                var c=new CommandData{present=true,identifier=entered,scope=f.kind==FieldKind.EffectId?(entered[0]=='Z'?CommandScope.Global:CommandScope.Track):CommandScope.Column};
                if(id){var before=f.kind==FieldKind.LocalId?n.sampleFx:x.command;if(before.identifier==entered){c.value=before.value;c.valuePresent=before.valuePresent;}if(initialValue.HasValue){c.value=initialValue.Value;c.valuePresent=true;}if(f.kind==FieldKind.LocalId)n.sampleFx=c;else x.command=c;}
                else{var value=f.kind==FieldKind.Volume?n.volume:n.pan;value.command=c;value.kind=ValueKind.Command;}
            }));
        }
    }
}
