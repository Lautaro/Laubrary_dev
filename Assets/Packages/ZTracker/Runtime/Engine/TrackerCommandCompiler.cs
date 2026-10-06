using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public sealed unsafe partial class TrackerPreparedSong
    {
        readonly Dictionary<string,int> memoryIds=new Dictionary<string,int>();
        static int Base36(char c)=>c>='0'&&c<='9'?c-'0':c>='A'&&c<='Z'?c-'A'+10:-1;
        static int Hex(char c)=>c>='0'&&c<='9'?c-'0':c>='A'&&c<='F'?c-'A'+10:-1;
        void CommandDiagnostic(string reason,CommandData c,string address)=>diagnostics.Add(reason+" "+address+" id="+c.identifier+" present="+c.present+" value="+c.value+" valuePresent="+c.valuePresent+" raw="+c.rawIdentifier+"/"+c.rawValue+" profile="+c.profile);
        bool CompileOp(CommandData c,int track,int column,int source,int priority,bool shorthand,string address,out TrackerOp op)
        {
            op=new TrackerOp{track=track,column=column,source=source,priority=priority,bank=-1,target=-1,shorthand=shorthand};
            if(!c.HasPayload)return false;
            if(c.profile!="renoise-p5-v1"&&c.profile!="native-v0"){CommandDiagnostic("UNSUPPORTED_COMMAND_PROFILE",c,address);return false;}
            if(c.profile=="native-v0"||c.hasLegacy&&c.profile!="renoise-p5-v1") {CommandDiagnostic("LEGACY_"+(c.legacyCommand==16?"MACRO_OWNERSHIP":c.legacyCommand==17?"MACRO_SLEW":c.legacyCommand==18?"PRESET_UNRESOLVED":c.legacyCommand==0x0b?"ORDER_JUMP":"PRESERVED_BYTE"),c,address);return false;}
            if(c.unsupported||!c.present||c.identifier==null||c.identifier.Length!=2||c.value<0||c.value>255){CommandDiagnostic("UNSUPPORTED_OR_MALFORMED",c,address);return false;}
            int value=c.valuePresent?c.value:0;string id=c.identifier;if(id[0]=='-')id="0"+id[1];
            if(!shorthand&&!c.valuePresent)CommandDiagnostic("IMPLICIT_ZERO",c,address);
            if(c.rawValue!=""&&(c.rawValue.Length!=2||Hex(c.rawValue[0])<0||Hex(c.rawValue[1])<0)){CommandDiagnostic("MALFORMED_VALUE",c,address);return false;}
            if(shorthand){int n=Hex(id[1]);if(n<0){CommandDiagnostic("MALFORMED_COLUMN_COMMAND",c,address);return false;}op.kind=id[0];value=n;
                string allowed=priority==4?"IOUDGCBQYR":"JKUDGCBQYR";if(!allowed.Contains(id[0])){CommandDiagnostic("COLUMN_COMMAND_OUT_OF_PLACE",c,address);return false;}
                if(id[0]=='C')op.kind='H';
            }else if(id[0]=='0'){
                op.kind=id[1];string allowed=column>=0?"AUDGVIOTCSBENY":"AUDGVIOTCSBENMQYRL PXJ";
                if(!allowed.Contains(id[1])||id[1]==' '){CommandDiagnostic("UNSUPPORTED_COMMAND",c,address);return false;}
            }else if(id[0]=='Z'&&column<0){op.kind=id[1]=='T'?256:id[1]=='L'?257:id[1]=='K'?258:id[1]=='G'?259:id[1]=='B'?260:id[1]=='D'?261:0;
                if(op.kind==0||(op.kind==256&&value!=0&&value<32)||(op.kind==258&&(value<1||value>16))||(op.kind==259&&value!=0)){CommandDiagnostic(op.kind==256&&value>=20?"BPM_RANGE_DISPUTE":"UNSUPPORTED_GLOBAL",c,address);return false;}
            }else if(column<0){int a=Base36(id[0]),b=Base36(id[1]);if(a<1||a>34||b<0){CommandDiagnostic("UNSUPPORTED_ADDRESS",c,address);return false;}
                int device=-1;for(int d=0;d<state.deviceCount;d++)if(state.devices[d].track==track&&state.devices[d].ordinal==a)device=d;
                if(device<0){CommandDiagnostic("DEVICE_UNRESOLVED",c,address);return false;}
                if(b==0){if(value>1){CommandDiagnostic("INVALID_BYPASS",c,address);return false;}op.kind=301;op.target=device;}
                else {var dev=state.devices[device];for(int p=0;p<dev.count;p++)if(state.deviceParameters[dev.parameters+p].ordinal==b)op.target=dev.parameters+p;if(op.target<0){CommandDiagnostic("PARAMETER_UNRESOLVED",c,address);return false;}op.kind=300;}
            }else{CommandDiagnostic("COMMAND_OUT_OF_PLACE",c,address);return false;}
            if(op.kind=='B'&&value>1||op.kind=='R'&&(value&15)==0||op.kind=='X'&&value!=0){CommandDiagnostic(op.kind=='R'?"ZERO_RETRIGGER_INTERVAL":"UNSUPPORTED_ARGUMENT",c,address);return false;}
            if(op.kind=='J'&&!shorthand){int route=state.master;if(value!=0){int ancestor=state.tracks[track].parent;for(int rank=1;rank<256-value&&ancestor>=0;rank++)ancestor=state.tracks[ancestor].parent;route=ancestor;}if(route<0||route==track){CommandDiagnostic("HARDWARE_ROUTE_UNSUPPORTED",c,address);return false;}op.target=route;}
            if(column<0&&op.kind<256&&op.kind!='L'&&op.kind!='P'&&op.kind!='X'&&op.kind!='J'&&state.tracks[track].kind!=TrackKind.Sequencer&&state.tracks[track].kind!=TrackKind.Master){CommandDiagnostic("COMMAND_SCOPE_UNSUPPORTED",c,address);return false;}
            if(column<0&&op.kind=='Y'&&state.tracks[track].kind!=TrackKind.Sequencer){CommandDiagnostic("PROBABILITY_SCOPE_UNSUPPORTED",c,address);return false;}
            if("UDGVIOTN".Contains((char)op.kind)) {string key=track+":"+source+":"+priority+":"+op.kind;if(!memoryIds.TryGetValue(key,out int bank)){bank=memoryIds.Count;memoryIds.Add(key,bank);}op.bank=bank;}
            op.value=value;return true;
        }
        void BuildCommandRows(SongData song,Dictionary<string,int> trackMap,List<TrackerPattern> patterns,List<TrackerRow> rows,List<TrackerCell> cells,List<TrackerAuthoredEvent> authored,Dictionary<string,int> pmap)
        {
            var ops=new List<TrackerOp>();
            foreach(var p in song.patterns){int pattern=patterns.Count;pmap.Add(p.id,pattern);patterns.Add(new TrackerPattern{rows=rows.Count,lineCount=p.lineCount});
                for(int line=0;line<p.lineCount;line++){
                    var rr=new TrackerRow{cells=cells.Count,events=authored.Count,ops=ops.Count};
                    foreach(var pt in p.tracks.OrderBy(pt=>trackMap[pt.trackId])){int ti=trackMap[pt.trackId];var src=pt.lines.Find(l=>l.line==line);if(src==null)continue;
                        string address="song="+song.id+" pattern="+p.id+" row="+line+" track="+ti;
                        bool midi=src.notes.Any(n=>!n.inactive&&n.pan.kind==ValueKind.Command&&n.pan.command.identifier!=null&&n.pan.command.identifier.StartsWith("M",StringComparison.Ordinal));
                        if(midi)diagnostics.Add("MIDI_PRESERVED_PAYLOAD "+address);
                        foreach(var n in src.notes.OrderBy(n=>n.column))if(!n.inactive){if(n.column>=state.tracks[ti].columnCount)throw new ArgumentException("Note column exceeds visible capacity");
                            var cell=new TrackerCell{track=ti,column=n.column,note=n.note==NoteKind.Note?n.pitch:n.note==NoteKind.Off?-2:-1,instrument=n.instrument,instrumentPresent=n.instrumentPresent,volume=n.volume.kind==ValueKind.Value&&n.volume.value<=128?n.volume.value:-1,pan=n.pan.kind==ValueKind.Value&&n.pan.value<=128?n.pan.value:-1,delay=n.delayPresent?n.delay:0,ops=ops.Count,parameterSet=parameterSetIds.TryGetValue(n.parameterSetId,out int set)?set:-1};
                            if(n.volume.kind==ValueKind.Value&&n.volume.value>128||n.pan.kind==ValueKind.Value&&n.pan.value>128)diagnostics.Add("INVALID_COLUMN_NUMERIC "+address+" column="+n.column);
                            if(n.instrumentPresent&&(n.instrument<0||n.instrument>=song.instruments.Count||song.instruments[n.instrument]==null))diagnostics.Add("INSTRUMENT_UNRESOLVED "+address+" column="+n.column+" instrument="+n.instrument);
                            CommandData[] cmd={n.sampleFx,n.volume.command,n.pan.command};for(int s=0;s<3;s++){if(s==1&&n.volume.kind!=ValueKind.Command||s==2&&n.pan.kind!=ValueKind.Command)continue;if(midi&&s==2&&cmd[s].identifier.StartsWith("M",StringComparison.Ordinal))continue;if(CompileOp(cmd[s],ti,n.column,n.column,3+s,s>0,address+" column="+n.column+" sub="+s,out var op))ops.Add(op);}
                            cell.opCount=ops.Count-cell.ops;cells.Add(cell);if(n.note==NoteKind.Legacy)diagnostics.Add("UNSUPPORTED_LEGACY_NOTE "+address);
                            if(n.parameterSetId!=""&&cell.parameterSet<0)diagnostics.Add("PARAMETER_SET_UNRESOLVED "+address);
                        }
                        if(!midi)foreach(var fx in src.effects.OrderBy(e=>e.column))if(!fx.inactive&&CompileOp(fx.command,ti,-1,fx.column,state.tracks[ti].kind==TrackKind.Master?1:2,false,address+" effect="+fx.column,out var op))ops.Add(op);
                        foreach(var ev in src.events)if(ev.present){int payload=eventPayloads.Count;eventPayloads.Add(ev.payload);authored.Add(new TrackerAuthoredEvent{track=ti,column=ev.column,payload=payload});}
                    }
                    rr.cellCount=cells.Count-rr.cells;rr.opCount=ops.Count-rr.ops;rr.eventCount=authored.Count-rr.events;rows.Add(rr);
                }
            }
            long occurrenceCapacity=(long)rows.Count*song.sequence.Count;if(occurrenceCapacity>1000000)throw new ArgumentException("Occurrence table capacity exceeded");
            state.ops=Native(ops);state.opCount=ops.Count;state.descriptors=Buffer<TrackerOp>(state.columnCount*16);state.commandMemory=Buffer<int>(memoryIds.Count);state.occurrences=Buffer<long>((int)occurrenceCapacity);state.seed=song.seed;
        }
    }
}
