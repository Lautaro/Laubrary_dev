using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public unsafe partial struct TrackerRealtime
    {
        static int Family(int k)=>k=='U'||k=='D'||k=='G'?0:k=='I'||k=='O'?1:k=='J'||k=='K'?2:k=='A'?3:k=='V'?4:k=='T'?5:k=='N'?6:k=='C'?7:k=='B'?8:k=='S'?9:k=='E'?10:k=='R'?11:k=='Q'?12:k=='Y'?13:k=='H'?14:-1;
        TrackerOp Descriptor(int column,int family)=>state.descriptors[column*16+family];
        void ResetCommandState()
        {
            for(int i=0;i<state.commandMemory.Length;i++)state.commandMemory[i]=0;
            for(int i=0;i<state.descriptors.Length;i++)state.descriptors[i]=default;
            for(int i=0;i<state.occurrences.Length;i++)state.occurrences[i]=0;
            state.holdRemaining=0;state.held=false;state.paused=false;ResetAutomation();
        }
        void ResolveRowCommands(in TrackerRow row)
        {
            for(int i=0;i<state.descriptors.Length;i++)state.descriptors[i]=default;
            for(int ci=0;ci<state.columnCount;ci++){var col=state.columns[ci];col.cutTick=col.volumeCutTick=-1;col.launchSuppressed=false;col.activeFrom=0;state.columns[ci]=col;}
            // Every source resolves its bank before priority is considered.
            for(int i=0;i<row.opCount;i++){var op=state.ops[row.ops+i];if(op.kind>=256||op.column<0&&(op.kind=='M'||op.kind=='L'||op.kind=='P'||op.kind=='X'||op.kind=='J'))continue;
                if(op.kind=='Q'&&!op.shorthand&&op.value>state.ticksPerLine){Emit(TrackerEventKind.Diagnostic,op.track,payload:(int)TrackerRuntimeDiagnostic.DelayOutsideRow);continue;}
                if(op.bank>=0){if(op.value!=0)state.commandMemory[op.bank]=op.value;else {op.value=state.commandMemory[op.bank];if(op.value==0){Emit(TrackerEventKind.Diagnostic,op.track,op.column,payload:(int)TrackerRuntimeDiagnostic.EmptyMemory);continue;}}}
                int family=Family(op.kind);if(family<0)continue;
                for(int t=0;t<state.trackCount;t++){if(op.column>=0&&t!=op.track||op.column<0&&state.tracks[op.track].kind!=TrackKind.Master&&t!=op.track)continue;if(state.tracks[t].kind!=TrackKind.Sequencer)continue;var tr=state.tracks[t];for(int c=0;c<tr.columnCount;c++){if(op.column>=0&&c!=op.column)continue;int at=(tr.columns+c)*16+family;var old=state.descriptors[at];if(old.kind==0||op.priority>=old.priority)state.descriptors[at]=op;}}
            }
            for(int ci=0;ci<state.columnCount;ci++){var col=state.columns[ci];var hard=Descriptor(ci,14);if(hard.kind!=0){if(hard.value>=state.ticksPerLine)Emit(TrackerEventKind.Diagnostic,hard.track,hard.column,payload:(int)TrackerRuntimeDiagnostic.CutOutsideRow);else{col.cutTick=hard.value;col.cutCohort=col.cohort;}}
                var cut=Descriptor(ci,7);if(cut.kind!=0){int tick=cut.value&15;if(tick>=state.ticksPerLine)Emit(TrackerEventKind.Diagnostic,cut.track,cut.column,payload:(int)TrackerRuntimeDiagnostic.CutOutsideRow);else{col.volumeCutTick=tick;col.volumeCutValue=cut.value>>4;col.volumeCutCohort=col.cohort;}}state.columns[ci]=col;
            }
            for(int i=0;i<row.cellCount;i++){var cell=state.cells[row.cells+i];int ci=state.tracks[cell.track].columns+cell.column;var col=state.columns[ci];if(cell.note!=-1||cell.instrumentPresent){var q=Descriptor(ci,12);double f=(q.kind!=0?q.value/(double)state.ticksPerLine:0)+cell.delay/256d;col.activeFrom=f;
                    if(f>=1){col.launchSuppressed=true;Emit(TrackerEventKind.Diagnostic,cell.track,cell.column,payload:(int)TrackerRuntimeDiagnostic.DelayOutsideRow);}if(cell.note>=0&&col.cutTick>=0&&col.cutTick/(double)state.ticksPerLine<=f)col.launchSuppressed=true;
                    col.cutCohort=col.volumeCutCohort=0;
                }state.columns[ci]=col;
            }
            ApplyProbability(in row);
            for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(!v.active||v.cohort!=state.columns[state.tracks[v.track].columns+v.column].cohort)continue;v.commandArp=v.commandVibrato=v.commandPan=0;v.commandTremolo=1;state.voices[i]=v;}
        }
        void RowGlobals(in TrackerRow row,bool flow=true)
        {
            state.breakRow=-1;if(flow)state.holdRemaining=0;
            for(int i=0;i<row.opCount;i++){var op=state.ops[row.ops+i];switch(op.kind){case 256:state.bpm=op.value;break;case 257:state.linesPerBeat=op.value;break;case 258:state.ticksPerLine=op.value;break;case 260:state.breakRow=op.value;break;case 261:if(flow)state.holdRemaining=op.value;break;}}
        }
        void RowScalarWrites(in TrackerRow row)
        {
            for(int i=0;i<state.deviceCount;i++)state.deviceDirty[i]=0;
            for(int i=0;i<state.deviceParameterCount;i++)state.parameterDirty[i]=0;
            for(int i=0;i<row.opCount;i++){var op=state.ops[row.ops+i];if(op.column>=0)continue;
                if(op.kind==300){var parameter=state.deviceParameters[op.target];parameter.latent=op.value/255f;state.deviceParameters[op.target]=parameter;state.parameterDirty[op.target]=1;continue;}
                if(op.kind==301){bool winner=true;for(int j=i+1;j<row.opCount;j++){var later=state.ops[row.ops+j];if(later.kind==301&&later.target==op.target)winner=false;}if(winner){state.deviceDirty[op.target]=1;SetDeviceEnabled(op.target,op.value!=0,false);}continue;}
                if(op.kind=='M'){for(int t=0;t<state.trackCount;t++)if(state.tracks[t].kind==TrackKind.Sequencer&&(t==op.track||state.tracks[op.track].kind==TrackKind.Master)){var tr=state.tracks[t];tr.instrumentGain=math.pow(10f,(-60+63*op.value/255f)/20);state.tracks[t]=tr;}}
                else if(op.kind=='L'){var t=state.tracks[op.track];t.preGain=op.value==0?0:math.pow(10f,(-60+63*(op.value-1)/254f)/20);state.tracks[op.track]=t;}
                else if(op.kind=='P'){var t=state.tracks[op.track];t.prePan=(op.value-128)/(op.value<=128?128f:127f);state.tracks[op.track]=t;}
                else if(op.kind=='J'){var t=state.tracks[op.track];t.output=op.target;state.tracks[op.track]=t;ReorderRoutes();}
                else if(op.kind=='X')StopContributors(op.track);
            }
            for(int p=0;p<state.deviceParameterCount;p++){var parameter=state.deviceParameters[p];if(state.parameterDirty[p]!=0||parameter.device>=0&&state.deviceDirty[parameter.device]!=0)EmitDeviceParameter(p);}
        }
        void ReorderRoutes()
        {
            // The destination is a stored ancestor/Master; no edge can introduce a cycle.
            for(int pass=0;pass<state.trackCount;pass++)for(int i=0;i<state.trackCount;i++){int source=state.order[i],dest=state.tracks[source].output;if(dest<0)continue;for(int j=0;j<i;j++)if(state.order[j]==dest){for(int k=j;k<i;k++)state.order[k]=state.order[k+1];state.order[i]=dest;break;}}
        }
        bool Contributes(int source,int target)
        {
            if(source==target)return true;for(int oi=state.trackCount-1;oi>=0;oi--){int t=state.order[oi];if(t!=source)continue;int dest=state.tracks[t].output;for(int n=0;n<state.trackCount&&dest>=0;n++){if(dest==target)return true;dest=state.tracks[dest].output;}var tr=state.tracks[t];for(int s=0;s<tr.sendCount;s++){dest=state.sends[tr.sendStart+s].destination;for(int n=0;n<state.trackCount&&dest>=0;n++){if(dest==target)return true;dest=state.tracks[dest].output;}}}return false;
        }
        void StopContributors(int target)
        {
            for(int ancestor=0;ancestor<state.trackCount;ancestor++)if(!Contributes(ancestor,target)&&Contributes(target,ancestor)&&state.tracks[ancestor].chainCount>0){bool shared=false;for(int source=0;source<state.trackCount;source++)if(state.tracks[source].kind==TrackKind.Sequencer&&Contributes(source,ancestor)&&!Contributes(source,target))shared=true;if(shared)Emit(TrackerEventKind.Diagnostic,ancestor,payload:(int)TrackerRuntimeDiagnostic.SharedFxStop);else {var t=state.tracks[ancestor];for(int c=0;c<t.chainCount;c++){ref var chain=ref state.chains[t.chainStart+c];chain.processor.Reset(in chain.layout,state.sampleRate,2463534242u,0);}}}
            for(int t=0;t<state.trackCount;t++)if(Contributes(t,target)){for(int i=0;i<state.voices.Length;i++)if(state.voices[i].active&&state.voices[i].track==t){var v=state.voices[i];v.active=false;state.voices[i]=v;}
                var tr=state.tracks[t];for(int c=0;c<tr.columnCount;c++){int ci=tr.columns+c;var col=state.columns[ci];col.cohort=0;col.instrument=-1;col.hasPreviousNote=false;col.due=-1;col.pendingCell=-1;state.columns[ci]=col;}
                for(int i=0;i<state.opCount;i++){var op=state.ops[i];if(op.track==t&&op.bank>=0)state.commandMemory[op.bank]=0;}
                for(int c=0;c<tr.chainCount;c++){ref var chain=ref state.chains[tr.chainStart+c];chain.processor.Reset(in chain.layout,state.sampleRate,2463534242u,0);}
                for(int b=0;b<state.busCount;b++)if(state.buses[b].track==t){ref var chain=ref state.chains[state.buses[b].chain];chain.processor.Reset(in chain.layout,state.sampleRate,2463534242u,0);}
            }
        }
        public static ulong CounterHash(ulong seed,ulong sequence,ulong pattern,ulong row,ulong occurrence,ulong track,ulong column,ulong purpose)
        {
            ulong h=14695981039346656037;for(int f=0;f<8;f++){ulong v=f==0?seed:f==1?sequence:f==2?pattern:f==3?row:f==4?occurrence:f==5?track:f==6?column:purpose;for(int b=0;b<8;b++){h=(h^(v&255))*1099511628211;v>>=8;}}ulong z=h+0x9E3779B97F4A7C15;z=(z^(z>>30))*0xBF58476D1CE4E5B9;z=(z^(z>>27))*0x94D049BB133111EB;return z^(z>>31);
        }
        double Draw(int track,ulong column,ulong purpose)=> (CounterHash(state.seed,(ulong)state.sequenceIndex,(ulong)state.sequence[state.sequenceIndex].pattern,(ulong)state.row,(ulong)state.rowOccurrence,(ulong)track,column,purpose)>>11)/9007199254740992d;
        bool Eligible(in TrackerCell cell)
        {
            var tr=state.tracks[cell.track];if(tr.triggerMute||!tr.soloEnabled||state.sequenceMutes[state.sequence[state.sequenceIndex].muteOffset+cell.track]!=0)return false;int ii=cell.instrumentPresent?cell.instrument:state.columns[tr.columns+cell.column].instrument;if(ii<0||ii>=state.instrumentCount)return false;var ins=state.instruments[ii];int velocity=cell.volume>=0?(int)math.floor(127*cell.volume/128d+.5):127;for(int z=0;z<ins.zoneCount;z++){var zone=state.zones[ins.zoneStart+z];if(cell.note>=zone.minNote&&cell.note<=zone.maxNote&&velocity>=zone.minVelocity&&velocity<=zone.maxVelocity)return true;}return false;
        }
        void ApplyProbability(in TrackerRow row)
        {
            for(int t=0;t<state.trackCount;t++){var gate=default(TrackerOp);for(int i=0;i<row.opCount;i++){var op=state.ops[row.ops+i];if(op.track==t&&op.column<0&&op.kind=='Y')gate=op;}bool exclusive=gate.kind!=0&&gate.value==0;int total=0,selected=-1;
                if(exclusive){for(int i=0;i<row.cellCount;i++){var cell=state.cells[row.cells+i];if(cell.track!=t||cell.note<0||!Eligible(in cell))continue;var y=Descriptor(state.tracks[t].columns+cell.column,13);int w=y.kind==0||y.column<0?255:y.shorthand?17*y.value:y.value;total+=w;}if(total>0){int bucket=(int)math.floor(Draw(t,ulong.MaxValue,3)*total),sum=0;for(int i=0;i<row.cellCount;i++){var cell=state.cells[row.cells+i];if(cell.track!=t||cell.note<0||!Eligible(in cell))continue;var y=Descriptor(state.tracks[t].columns+cell.column,13);sum+=y.kind==0||y.column<0?255:y.shorthand?17*y.value:y.value;if(bucket<sum){selected=cell.column;break;}}}else Emit(TrackerEventKind.Diagnostic,t,payload:(int)TrackerRuntimeDiagnostic.ExclusiveNoWeight);}
                bool trackPass=gate.kind==0||exclusive||Draw(t,ulong.MaxValue,1)<gate.value/255d;
                for(int i=0;i<row.cellCount;i++){var cell=state.cells[row.cells+i];if(cell.track!=t||cell.note==-1&&!cell.instrumentPresent)continue;int ci=state.tracks[t].columns+cell.column;var col=state.columns[ci];var y=Descriptor(ci,13);if(!trackPass||exclusive&&cell.note>=0&&cell.column!=selected)col.launchSuppressed=true;if(!exclusive&&y.kind!=0&&y.column>=0){if(y.shorthand||y.value!=0)col.launchSuppressed|=Draw(t,(ulong)cell.column,2)>=y.value/(y.shorthand?15d:255d);}state.columns[ci]=col;}
            }
        }
        void InitializeColumn(int ci,bool newNote)
        {
            var col=state.columns[ci];for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(!v.active||v.cohort!=col.cohort)continue;bool sampler=state.tones[v.sample].kind==0;
                if(newNote){v.commandGain=v.commandTremolo=v.retriggerGain=1;v.commandPitch=v.commandArp=v.commandVibrato=v.commandPan=0;v.phaseV=v.phaseT=v.phaseN=0;}
                if(sampler){var s=state.samples[v.sample];var b=Descriptor(ci,8);var offset=Descriptor(ci,9);if(b.kind!=0){v.direction=b.value==0?-1:1;if(newNote&&offset.kind==0)v.position=v.direction<0?v.regionEnd-1:v.regionStart;}
                    if(offset.kind!=0){int start=s.regionStart,end=s.regionEnd;bool valid=true;if(s.sliceCount>0){if(offset.value>s.sliceCount){valid=false;Emit(TrackerEventKind.Diagnostic,v.track,v.column,payload:(int)TrackerRuntimeDiagnostic.SliceOutside);}else if(offset.value>0){start=state.sliceMarkers[s.slices+offset.value-1];end=offset.value<s.sliceCount?state.sliceMarkers[s.slices+offset.value]:s.regionEnd;}}else start=s.regionStart+(int)math.floor((s.regionEnd-s.regionStart)*(offset.value/256d));if(valid){v.regionStart=s.sliceCount>0?start:s.regionStart;v.regionEnd=end;v.position=v.selectedStart=math.min(start,end-1);v.explicitStart=true;}}
                    var e=Descriptor(ci,10);if(e.kind!=0)SeekEnvelopeClocks(i,ref v,e.value/256d);
                    var g=Descriptor(ci,0);if(g.kind=='G'&&col.glideTarget>=0&&(g.value==255&&!g.shorthand||g.value==15&&g.shorthand))v.commandPitch=math.clamp(col.glideTarget-v.note,-1536,1536);
                    if(col.volumeCutTick>=0&&col.volumeCutTick/(double)state.ticksPerLine<=col.activeFrom)v.commandGain=col.volumeCutValue/15f;
                }state.voices[i]=v;
            }
            if(newNote){col.cutCohort=col.volumeCutCohort=col.cohort;state.columns[ci]=col;}
        }
        void HardCut(long cohort){if(cohort==0)return;for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.active&&v.cohort==cohort){v.active=false;state.voices[i]=v;}}}
        void CommandTick()
        {
            if(state.held)return;
            for(int ci=0;ci<state.columnCount;ci++){var col=state.columns[ci];if(col.cutTick==state.tick)HardCut(col.cutCohort);
                for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(!v.active||v.cohort!=col.cohort)continue;bool sampler=state.tones[v.sample].kind==0;
                    if(col.volumeCutTick==state.tick&&v.cohort==col.volumeCutCohort&&sampler)v.commandGain=col.volumeCutValue/15f;
                    if(state.tick/(double)state.ticksPerLine+1e-15<col.activeFrom) {state.voices[i]=v;continue;}
                    var r=Descriptor(ci,11);if(r.kind!=0&&state.tick>0&&state.tick%(r.value&15)==0&&!state.tracks[v.track].triggerMute&&state.tracks[v.track].soloEnabled&&state.sequenceMutes[state.sequence[state.sequenceIndex].muteOffset+v.track]==0)Retrigger(i,ref v,in r);
                    if(sampler){var pitch=Descriptor(ci,0);float amount=pitch.value/(float)state.ticksPerLine/(pitch.shorthand?1:16);if(pitch.kind=='U')v.commandPitch+=amount;else if(pitch.kind=='D')v.commandPitch-=amount;else if(pitch.kind=='G'&&col.glideTarget>=0){float distance=col.glideTarget-v.note-v.commandPitch;v.commandPitch+=math.clamp(distance,-amount,amount);}
                        if(math.abs(v.commandPitch)>1536){v.commandPitch=math.clamp(v.commandPitch,-1536,1536);if(!v.pitchLimited){v.pitchLimited=true;Emit(TrackerEventKind.Diagnostic,v.track,v.column,payload:(int)TrackerRuntimeDiagnostic.PitchLimit);}}
                        var gain=Descriptor(ci,1);amount=gain.value/(float)state.ticksPerLine/(gain.shorthand?16:256);if(gain.kind=='I')v.commandGain=math.min(1,v.commandGain+amount);else if(gain.kind=='O')v.commandGain=math.max(0,v.commandGain-amount);
                        var pan=Descriptor(ci,2);if(pan.kind!=0)v.pan=math.clamp(v.pan+(pan.kind=='J'?-1:1)*pan.value/(64f*state.ticksPerLine),-1,1);
                        var a=Descriptor(ci,3);v.commandArp=a.kind==0?0:state.tick%3==1?a.value>>4:state.tick%3==2?a.value&15:0;
                        LfoTick(Descriptor(ci,4),ref v.phaseV,out v.commandVibrato,0);LfoTick(Descriptor(ci,5),ref v.phaseT,out v.commandTremolo,1);LfoTick(Descriptor(ci,6),ref v.phaseN,out v.commandPan,2);
                    }state.voices[i]=v;
                }
            }
        }
        void LfoTick(TrackerOp op,ref float phase,out float output,int kind)
        {
            if(op.kind==0){output=kind==1?1:0;return;}float depth=(op.value&15)/(kind==0?16f:15f);output=kind==1?1-depth*(1-math.cos(phase))*.5f:depth*math.sin(phase);phase+=(op.value>>4)*2*math.PI/(16*state.ticksPerLine);phase-=math.floor(phase/(2*math.PI))*2*math.PI;
        }
        public static float RetriggerFactor(float gain,int x)=>math.clamp(x==1?gain-.03f:x==2?gain-.06f:x==3?gain-.12f:x==4?gain-.25f:x==5?gain-.5f:x==6?gain*.67f:x==7?gain*.5f:x==9?gain+.03f:x==10?gain+.06f:x==11?gain+.12f:x==12?gain+.25f:x==13?gain+.5f:x==14?gain*1.5f:x==15?gain*2:gain,0,1);
        void Retrigger(int index,ref TrackerVoice v,in TrackerOp op)
        {
            bool sampler=state.tones[v.sample].kind==0;if(sampler){if(!op.shorthand)v.position=v.explicitStart?v.selectedStart:v.direction<0?v.regionEnd-1:v.regionStart;v.commandGain=RetriggerFactor(v.commandGain,op.shorthand?0:op.value>>4);}else v.retriggerGain=RetriggerFactor(v.retriggerGain,op.shorthand?0:op.value>>4);
            v.released=false;v.age=v.releaseAge=0;v.envelopePosition=0;v.stage=0;v.envelope=0;v.phaseA=v.phaseB=v.fmPrevious=0;v.fmPhase=v.fmLevel=default;v.fmStage=default;v.blendStage=0;v.blendLevel=0;v.envTime=0;v.fL1=v.fL2=v.fR1=v.fR2=v.xL1=v.xL2=v.xR1=v.xR2=0;v.phaseV=v.phaseT=v.phaseN=0;for(int m=0;m<state.modStride;m++)state.modulationState[index*state.modStride+m]=default;
        }
        void SeekEnvelopeClocks(int index,ref TrackerVoice v,double u)
        {
            var s=state.samples[v.sample];if(v.stage!=5){if(v.released){v.envelopePosition=u*s.release;v.envelope=v.releaseStart*(1-(float)u);}else {double time=u*(s.attack+s.hold+s.decay);v.envelopePosition=time;v.envelope=time<s.attack?s.attack>0?(float)(time/s.attack):1:time<s.attack+s.hold?1:time<s.attack+s.hold+s.decay?1-(1-s.sustain)*(float)((time-s.attack-s.hold)/math.max(1e-9,s.decay)):s.sustain;v.stage=time<s.attack?0:time<s.attack+s.hold?1:time<s.attack+s.hold+s.decay?2:3;}}
            for(int i=0;i<s.modCount;i++){var m=state.mods[s.modStart+i];int at=index*state.modStride+i;var ms=state.modulationState[at];if(ms.finished)continue;if(m.kind==ModulationDeviceKind.Multipoint&&m.pointCount>0)ms.position=u*state.points[m.points+m.pointCount-1].time;else if(m.kind==ModulationDeviceKind.AHDSR)ms.position=u*(v.released?m.release:m.attack+m.hold+m.decay);else if(m.kind==ModulationDeviceKind.Fader&&m.fadeSeconds>0)ms.position=u*m.fadeSeconds;else continue;ms.seeked=true;state.modulationState[at]=ms;}
        }
    }
}
