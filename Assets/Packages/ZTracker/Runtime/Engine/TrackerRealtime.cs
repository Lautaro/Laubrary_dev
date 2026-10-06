using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Burst;
using Unity.Collections;
using Unity.IntegerTime;
using Unity.Mathematics;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Single song clock, sampler pool and bus graph. Offline and SAP call this identical struct.</summary>
    [BurstCompile(CompileSynchronously=true,FloatMode=FloatMode.Strict)]
    public unsafe partial struct TrackerRealtime : GeneratorInstance.IRealtime
    {
        public TrackerState state;
        public TrackerEventRing events;
        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;
        public void Update(UpdatedDataContext context,Pipe pipe)
        {
            foreach(var element in pipe.GetAvailableData(context))if(element.TryGetData(out TrackerCommand command))Apply(in command);
        }
        public GeneratorInstance.Result Process(in RealtimeContext context,Pipe pipe,ChannelBuffer buffer,GeneratorInstance.Arguments args)
        {
            int at=0;
            while(at<buffer.frameCount){int n=math.min(state.maxFrames,buffer.frameCount-at);Render(n);
                for(int i=0;i<n;i++){float l=state.outputLeft[i],r=state.outputRight[i];if(buffer.channelCount==1)buffer[0,at+i]=(l+r)*.5f;else {buffer[0,at+i]=l;buffer[1,at+i]=r;for(int c=2;c<buffer.channelCount;c++)buffer[c,at+i]=0;}}at+=n;
            }
            return buffer.frameCount;
        }
        public void Apply(in TrackerCommand c)
        {
            switch(c.kind){
                case TrackerCommandKind.Play:
                    ClearVoices();ResetChains();ResetDefaults();ResetCommandState();state.sequenceIndex=0;state.row=0;state.tick=0;state.breakRow=-1;state.rowStart=0;state.clockCompensation=0;state.transportOrigin=state.samplePosition;state.rowPending=true;state.playing=true;state.loopSong=c.a!=0;ResetColumns();Emit(TrackerEventKind.Started);break;
                case TrackerCommandKind.Stop:
                    state.playing=false;state.rowPending=true;ClearVoices();ResetChains();ResetColumns();ResetCommandState();Emit(TrackerEventKind.Stopped);break;
                case TrackerCommandKind.Pause:state.paused=true;break;
                case TrackerCommandKind.Resume:state.paused=false;break;
                case TrackerCommandKind.ReleaseAll:
                    for(int i=0;i<state.voices.Length;i++)Release(i);break;
                case TrackerCommandKind.Seek:
                    if(c.a<0||c.a>=state.sequence.Length)return;
                    int p=state.sequence[c.a].pattern;if(c.b<0||c.b>=state.patterns[p].lineCount)return;
                    Seek(c.a,c.b);break;
                case TrackerCommandKind.AuditionOn:
                    if(c.d>=0&&c.d<state.trackCount&&state.tracks[c.d].columnCount>0&&c.b>=0&&c.b<120&&c.c>=0&&c.c<=127){int ci=state.tracks[c.d].columns;var col=state.columns[ci];col.volume=c.c/127f;state.columns[ci]=col;NoteOn(c.d,0,c.a,c.b,c.c);}break;
                case TrackerCommandKind.AuditionNormalized:
                    if(c.d>=0&&c.d<state.trackCount&&state.tracks[c.d].columnCount>0&&c.b>=0&&c.b<120&&c.c>=0&&c.c<=127&&math.isfinite(c.value)&&c.value>=0&&c.value<=1){int ci=state.tracks[c.d].columns;var col=state.columns[ci];col.volume=c.value;state.columns[ci]=col;NoteOn(c.d,0,c.a,c.b,c.c);}break;
                case TrackerCommandKind.ReleaseVoice:
                    if(c.a>=0&&c.a<state.voices.Length&&state.voices[c.a].active&&state.voices[c.a].cohort==c.generation)for(int i=0;i<state.voices.Length;i++)if(state.voices[i].active&&state.voices[i].cohort==c.generation)Release(i);break;
                case TrackerCommandKind.AuditionOff:
                    if(c.a>=0&&c.a<state.trackCount&&c.b>=0&&c.b<state.tracks[c.a].columnCount)Off(c.a,c.b);break;
                case TrackerCommandKind.TrackGain:
                    if(c.a>=0&&c.a<state.trackCount&&math.isfinite(c.value)&&c.value>=0&&c.value<=16){var t=state.tracks[c.a];t.postGain=c.value;t.gainRemaining=64;t.gainStep=(c.value-t.livePostGain)/64;state.tracks[c.a]=t;}break;
                case TrackerCommandKind.TrackPan:
                    if(c.a>=0&&c.a<state.trackCount&&math.isfinite(c.value)&&math.abs(c.value)<=1){var t=state.tracks[c.a];t.postPan=c.value;t.panRemaining=64;t.panStep=(c.value-t.livePostPan)/64;state.tracks[c.a]=t;}break;
                case TrackerCommandKind.TrackMute:
                    if(c.a>=0&&c.a<state.trackCount){var t=state.tracks[c.a];t.outputMute=c.b!=0;t.triggerMute=c.c!=0;state.tracks[c.a]=t;}break;
                case TrackerCommandKind.Swap:
                    // Update runs between blocks. The old ticket becomes terminal only after its last reader returned.
                    SapRenderTicket.Enter(state.ticket);SapRenderTicket.Exit(state.ticket,0,true);
                    long position=state.samplePosition,generation=state.cohort;state=c.replacement;state.samplePosition=position;state.cohort=generation;state.transportOrigin=position;state.rowPending=true;Emit(TrackerEventKind.PreparedSwap);break;
                default:ApplyParameterCommand(in c);break;
            }
        }
        void ResetColumns(){for(int i=0;i<state.columnCount;i++)state.columns[i]=new TrackerColumn{instrument=-1,volume=1,pan=0,due=-1,pendingCell=-1,glideTarget=-1,cutTick=-1,volumeCutTick=-1};}
        void ClearVoices(){for(int i=0;i<state.voices.Length;i++)state.voices[i]=default;for(int i=0;i<state.modulationState.Length;i++)state.modulationState[i]=default;}
        void ResetChains(){for(int i=0;i<state.chainCount;i++){ref var c=ref state.chains[i];c.processor.Reset(in c.layout,state.sampleRate,2463534242u,0);TrackerChainSeed.Apply(ref c.processor,in c.layout);}}
        void ResetDefaults(){state.bpm=state.authoredBpm;state.linesPerBeat=state.authoredLinesPerBeat;state.ticksPerLine=state.authoredTicksPerLine;state.normalization=1;for(int i=0;i<state.trackCount;i++){var t=state.tracks[i];t.livePostGain=t.postGain=t.authoredPostGain;t.livePostPan=t.postPan=t.authoredPostPan;t.outputMute=t.authoredOutputMute;t.triggerMute=t.authoredTriggerMute;t.gainRemaining=t.panRemaining=0;t.output=t.authoredOutput;t.instrumentGain=1;state.tracks[i]=t;}ResetToneDefaults();}
        void ClockCommands(in TrackerRow row)=>RowGlobals(in row);
        void Seek(int sequence,int row)
        {
            bool resume=state.playing;bool discarded=false;ClearVoices();ResetChains();ResetColumns();ResetDefaults();ResetCommandState();state.playing=false;state.sequenceIndex=0;state.row=0;state.tick=0;state.rowStart=0;state.clockCompensation=0;state.silentReplay=true;
            for(int visited=0;visited<1000000;visited++){
                if(state.sequenceIndex==sequence&&state.row==row){state.silentReplay=false;state.transportOrigin=state.samplePosition-Quantize(state.rowStart);state.rowPending=true;state.playing=resume;if(discarded)Emit(TrackerEventKind.Diagnostic,payload:(int)TrackerRuntimeDiagnostic.SeekVoiceStateDiscarded);return;}
                var pattern=state.patterns[state.sequence[state.sequenceIndex].pattern];var rr=state.rows[pattern.rows+state.row];EvaluateAutomation(state.row,true);RowGlobals(in rr);if(state.bpm==0||state.linesPerBeat==0)break;
                int occurrence=state.sequenceIndex*state.rows.Length+pattern.rows+state.row;state.rowOccurrence=state.occurrences[occurrence]++;
                EvaluateAutomation(state.row);ResolveRowCommands(in rr);RowScalarWrites(in rr);
                double evaluated=state.row;
                int tick=1;
                while(true){double next=tick<state.ticksPerLine?state.row+tick/(double)state.ticksPerLine:state.row+1;
                    for(int ai=0;ai<state.automationCount;ai++){var lane=state.automation[ai];if(lane.pattern!=state.sequence[state.sequenceIndex].pattern||lane.timing)continue;for(int n=0;n<lane.count;n++){double time=state.automationPoints[lane.points+n].line;if(time>evaluated&&time<next)next=time;}}
                    if(next>=state.row+1)break;EvaluateAutomation(next);if(tick<state.ticksPerLine&&next==state.row+tick/(double)state.ticksPerLine)tick++;evaluated=next;
                }
                if(state.holdRemaining>0)EvaluateAutomation(state.row+1);
                for(int i=0;i<rr.cellCount;i++){var c=state.cells[rr.cells+i];int ci=state.tracks[c.track].columns+c.column;var col=state.columns[ci];if(c.note>=0)discarded=true;if(!col.launchSuppressed&&c.instrumentPresent&&c.instrument>=0&&c.instrument<state.instrumentCount&&(c.note<0||Eligible(in c)))col.instrument=c.instrument;state.columns[ci]=col;}
                double duration=state.sampleRate*60d/(state.bpm*state.linesPerBeat);
                for(int held=0;held<=state.holdRemaining;held++)
                {
                    double compensation=state.clockCompensation;
                    double end=ClockEnd(state.rowStart,duration,ref compensation);
                    if(!ClockValid(state.rowStart,end,duration,compensation))
                    {
                        state.silentReplay=false;state.playing=false;
                        Emit(TrackerEventKind.Diagnostic,payload:(int)(!math.isfinite(duration)||duration<1?TrackerRuntimeDiagnostic.InvalidClock:TrackerRuntimeDiagnostic.ClockLimit));
                        return;
                    }
                    state.rowStart=end;state.clockCompensation=compensation;
                }
                state.holdRemaining=0;
                state.row++;
                if(state.breakRow>=0||state.row>=pattern.lineCount){state.sequenceIndex++;if(state.sequenceIndex>=state.sequence.Length)state.sequenceIndex=0;var dest=state.patterns[state.sequence[state.sequenceIndex].pattern];state.row=state.breakRow>=0?math.min(state.breakRow,dest.lineCount-1):0;}
            }
            state.silentReplay=false;state.playing=false;Emit(TrackerEventKind.Diagnostic,payload:(int)TrackerRuntimeDiagnostic.SeekUnreachable);
        }
        void Emit(TrackerEventKind kind,int track=-1,int column=-1,int note=-1,int instrument=-1,int payload=-1)
        {
            if(events.counters==null||state.silentReplay)return;
            events.Write(new TrackerEvent{kind=kind,samplePosition=state.samplePosition,sequence=state.sequenceIndex,pattern=state.sequence[state.sequenceIndex].pattern,row=state.row,tick=state.tick,track=track,column=column,note=note,instrument=instrument,payload=payload});
        }
        // Binary64 quantizer from COMMANDS.md P-CLOCK; no floating epsilon tied to musical time.
        public static bool ClockValid(double start,double end,double duration,double compensation)=>math.isfinite(duration)&&duration>=1&&math.isfinite(start)&&start>=0&&math.isfinite(end)&&end<=1099511627776d&&math.isfinite(compensation)&&Quantize(end)>Quantize(start);
        public static double ClockEnd(double start,double duration,ref double compensation)
        {
            double corrected=duration-compensation;
            double end=start+corrected;
            compensation=(end-start)-corrected;
            return end;
        }
        public static long Quantize(double x)
        {
            double n=math.round(x);long bits=math.aslong(math.max(1,math.abs(x)));int exponent=(int)((bits>>52)&2047)-1023;
            double ulp=math.asdouble((long)(exponent-52+1023)<<52);
            return (long)(math.abs(x-n)<=8*ulp?n:math.floor(x));
        }
        void EnterRow()
        {
            var seq=state.sequence[state.sequenceIndex];var pat=state.patterns[seq.pattern];var rr=state.rows[pat.rows+state.row];state.tick=0;state.breakRow=-1;state.held=false;
            int occurrence=state.sequenceIndex*state.rows.Length+pat.rows+state.row;state.rowOccurrence=state.occurrences[occurrence]++;
            EvaluateAutomation(state.row,true);ClockCommands(in rr);
            if(state.bpm==0||state.linesPerBeat==0){var stop=TrackerCommand.Stop();Apply(in stop);return;}
            double duration=(state.sampleRate*60d)/(state.bpm*state.linesPerBeat);state.rowDuration=duration;
            state.rowEnd=ClockEnd(state.rowStart,duration,ref state.clockCompensation);
            if(!ClockValid(state.rowStart,state.rowEnd,duration,state.clockCompensation)){state.playing=false;ClearVoices();Emit(TrackerEventKind.Diagnostic,payload:(int)(!math.isfinite(duration)||duration<1?TrackerRuntimeDiagnostic.InvalidClock:TrackerRuntimeDiagnostic.ClockLimit));Emit(TrackerEventKind.Stopped);return;}
            state.tickDeadline=state.rowStart+duration/state.ticksPerLine;
            ResolveRowCommands(in rr);EvaluateAutomation(state.row);RowScalarWrites(in rr);ScheduleAutomationPoint(state.row);
            Emit(TrackerEventKind.Row);Emit(TrackerEventKind.Tick);
            if(state.beatTicks&&state.row%state.beatInterval==0)Emit(TrackerEventKind.Beat);
            for(int t=0;t<state.trackCount;t++)if(state.tracks[t].beatTicks&&state.row%state.tracks[t].beatInterval==0&&!Muted(t))Emit(TrackerEventKind.Beat,t);
            for(int i=0;i<rr.eventCount;i++){var e=state.authoredEvents[rr.events+i];if(!Muted(e.track))Emit(TrackerEventKind.Authored,e.track,e.column,payload:e.payload);}
            for(int ci=0;ci<state.columnCount;ci++){bool hasCell=false;for(int i=0;i<rr.cellCount;i++){var cell=state.cells[rr.cells+i];if(state.tracks[cell.track].columns+cell.column==ci){hasCell=true;break;}}if(!hasCell)InitializeColumn(ci,false);}
            for(int i=0;i<rr.cellCount;i++){int index=rr.cells+i;var c=state.cells[index];int ci=state.tracks[c.track].columns+c.column;var col=state.columns[ci];if(col.launchSuppressed)continue;
                if(col.activeFrom>0&&(c.note!=-1||c.instrumentPresent)){col.dueExact=state.rowStart+col.activeFrom*duration;col.due=state.transportOrigin+Quantize(col.dueExact);col.pendingCell=index;state.columns[ci]=col;}else ApplyCell(in c);
            }
            CommandTick();state.rowPending=false;
        }
        void AdvanceTick()
        {
            state.tick++;
            if(state.tick>=state.ticksPerLine){
                state.tick=0;
                if(state.holdRemaining>0)
                {
                    if(!state.held)EvaluateAutomation(state.row+1);
                    double start=state.rowEnd,compensation=state.clockCompensation;
                    double end=ClockEnd(start,state.rowDuration,ref compensation);
                    if(!ClockValid(start,end,state.rowDuration,compensation))
                    {
                        state.playing=false;ClearVoices();
                        Emit(TrackerEventKind.Diagnostic,payload:(int)TrackerRuntimeDiagnostic.ClockLimit);
                        Emit(TrackerEventKind.Stopped);
                        return;
                    }
                    state.holdRemaining--;state.held=true;
                    state.rowStart=start;state.rowEnd=end;state.clockCompensation=compensation;
                    state.tickDeadline=start+state.rowDuration/state.ticksPerLine;
                    state.automationDeadline=double.PositiveInfinity;
                    for(int ci=0;ci<state.columnCount;ci++)
                    {
                        var col=state.columns[ci];col.pendingCell=-1;col.due=-1;state.columns[ci]=col;
                    }
                    return;
                }
                state.row++;var p=state.patterns[state.sequence[state.sequenceIndex].pattern];
                if(state.breakRow>=0||state.row>=p.lineCount){state.sequenceIndex++;if(state.sequenceIndex>=state.sequence.Length){if(!state.loopSong&&state.breakRow<0){state.sequenceIndex=state.sequence.Length-1;state.row=p.lineCount-1;state.playing=false;Emit(TrackerEventKind.Stopped);return;}state.sequenceIndex=0;}
                    int lines=state.patterns[state.sequence[state.sequenceIndex].pattern].lineCount;if(state.breakRow>=lines)Emit(TrackerEventKind.Diagnostic,payload:(int)TrackerRuntimeDiagnostic.BreakClamped);state.row=state.breakRow>=0?math.min(state.breakRow,lines-1):0;
                }
                state.rowStart=state.rowEnd;state.rowPending=true;EnterRow();
            }else {if(!state.held){EvaluateAutomation(state.row+state.tick/(double)state.ticksPerLine);AdvanceMacroTicks(-1);CommandTick();}state.tickDeadline=state.rowStart+((state.tick+1)/(double)state.ticksPerLine)*state.rowDuration;Emit(TrackerEventKind.Tick);}
        }
        bool Muted(int track)=>state.tracks[track].outputMute||!state.tracks[track].soloEnabled||state.sequenceMutes[state.sequence[state.sequenceIndex].muteOffset+track]!=0;
        void ApplyCell(in TrackerCell cell)
        {
            int ci=state.tracks[cell.track].columns+cell.column;
            var previous=state.columns[ci];var col=previous;col.pendingCell=-1;col.due=-1;state.columns[ci]=col;
            int instrument=(cell.note!=-1||cell.instrumentPresent)?col.pendingInstrument:col.instrument;
            if((cell.note>=0||cell.instrumentPresent)&&(instrument<0||instrument>=state.instrumentCount||state.instruments[instrument].zoneCount==0)){
                Emit(TrackerEventKind.Diagnostic,cell.track,cell.column,payload:(int)TrackerRuntimeDiagnostic.InstrumentUnresolved);return;
            }
            var pitch=Descriptor(ci,0);bool suitable=false;bool sameInstrument=true;
            for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.active&&v.cohort==col.cohort&&state.tones[v.sample].kind==0){suitable=true;sameInstrument &= v.instrument==instrument;}}
            bool glide=cell.note>=0&&pitch.kind=='G'&&suitable&&sameInstrument;
            if(cell.note>=0&&pitch.kind=='G'&&!glide)Emit(TrackerEventKind.Diagnostic,cell.track,cell.column,payload:(int)(suitable?TrackerRuntimeDiagnostic.GlideInstrumentChanged:TrackerRuntimeDiagnostic.GlideWithoutVoice));
            if(cell.instrumentPresent)col.instrument=instrument;
            if(cell.note>=0&&!glide){col.volume=1;col.pan=0;col.glideTarget=pitch.kind=='G'?cell.note:-1;}
            if(cell.volume>=0)col.volume=cell.volume/128f;
            if(cell.pan>=0)col.pan=cell.pan/64f-1;
            if(glide)col.glideTarget=cell.note;
            state.columns[ci]=col;
            if(cell.note==-2)Off(cell.track,cell.column);
            else if(cell.note>=0&&!glide){
                NoteOn(cell.track,cell.column,instrument,cell.note,math.clamp((int)math.floor(col.volume*127+.5f),0,127),cell.parameterSet);
                if(state.columns[ci].cohort==previous.cohort){previous.pendingCell=-1;previous.due=-1;state.columns[ci]=previous;return;}
            }
            else for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.active&&v.cohort==col.cohort){if(cell.volume>=0)v.volume=col.volume;if(cell.pan>=0)v.pan=col.pan;state.voices[i]=v;}}
            col=state.columns[ci];col.localsPending=false;state.columns[ci]=col;
            InitializeColumn(ci,cell.note>=0&&!glide);
        }
        void Off(int track,int column)
        {
            long cohort=state.columns[state.tracks[track].columns+column].cohort;
            for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.active&&v.track==track&&v.column==column&&v.cohort==cohort&&!state.samples[v.sample].oneShot)Release(i);}
            Emit(TrackerEventKind.NoteOff,track,column);
        }
        void Release(int i)
        {
            var v=state.voices[i];if(!v.active||v.released)return;var s=state.samples[v.sample];float release=Written(v.instrument,TrackerParameter.Release)?VoiceParameter(in v,TrackerParameter.Release):s.release;v.released=true;v.releaseAge=v.age;v.releaseStart=v.envelope;v.releaseStep=release>0?v.envelope/(release*state.sampleRate):1;v.stage=4;
            if(s.releaseExitsLoop&&s.loop!=SampleLoop.Off)v.direction=1;
            var tone=state.tones[v.sample];
            if(tone.releaseExitsLoopB&&tone.loopB!=SampleLoop.Off)v.directionB=1;
            state.voices[i]=v;
        }
        void NoteOn(int track,int column,int instrument,int note,int velocity,int parameterSet=-1)
        {
            if(instrument<0||instrument>=state.instrumentCount||note<0||note>119||state.tracks[track].triggerMute||!state.tracks[track].soloEnabled||state.sequenceMutes[state.sequence[state.sequenceIndex].muteOffset+track]!=0)return;
            var ins=state.instruments[instrument];if(parameterSet>=0){var preset=state.parameterSets[parameterSet];if(preset.instrument!=instrument)return;ins.zoneStart=preset.zones;ins.zoneCount=preset.zoneCount;}if(ins.zoneCount==0)return;
            bool mapped=false;int required=0;for(int z=0;z<ins.zoneCount;z++){var zone=state.zones[ins.zoneStart+z];if(note>=zone.minNote&&note<=zone.maxNote&&velocity>=zone.minVelocity&&velocity<=zone.maxVelocity&&(state.samples[zone.sample].pcm>=0||state.tones[zone.sample].kind!=0)){mapped=true;required+=state.tones[zone.sample].members;}}if(!mapped||required>state.voices.Length)return;
            EvaluateParameters();
            // NNA applies only to the foreground cohort in this note column; layered siblings remain a bundle.
            int ci=state.tracks[track].columns+column;var col=state.columns[ci];
            bool previousHeld=false;for(int i=0;i<state.voices.Length;i++)if(state.voices[i].active&&!state.voices[i].released&&state.voices[i].cohort==col.cohort)previousHeld=true;
            for(int i=0;i<state.voices.Length;i++){var old=state.voices[i];if(old.active&&old.track==track&&old.column==column&&old.cohort==col.cohort){var nna=state.samples[old.sample].nna;if(nna==NewNoteAction.Cut){old.active=false;state.voices[i]=old;}else if(nna==NewNoteAction.NoteOff)Release(i);}}
            long cohort=++state.cohort;col.cohort=cohort;col.instrument=instrument;state.columns[ci]=col;
            for(int z=0;z<ins.zoneCount;z++){
                var zone=state.zones[ins.zoneStart+z];if(note<zone.minNote||note>zone.maxNote||velocity<zone.minVelocity||velocity>zone.maxVelocity)continue;
                var sample=state.samples[zone.sample];var tone=state.tones[zone.sample];if(sample.pcm<0&&tone.kind==0)continue;
                if(sample.muteGroup>0)for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.active&&v.track==track&&v.instrument==instrument&&v.cohort!=cohort&&state.samples[v.sample].muteGroup==sample.muteGroup){v.active=false;state.voices[i]=v;}}
                for(int member=0;member<tone.members;member++){
                int index=ChooseVoice(cohort);var clip=sample.pcm>=0?state.clips[sample.pcm]:default;
                int bus=-1;if(sample.fxChain>=0)for(int b=0;b<state.busCount;b++){var route=state.buses[b];if(route.track==track&&route.instrument==instrument&&route.fx==sample.fxChain){bus=b;break;}}
                var voice=new TrackerVoice{active=true,sample=zone.sample,instrument=instrument,track=track,column=column,note=note,velocity=velocity,cohort=cohort,volume=col.volume,pan=col.pan,bus=bus,direction=sample.loop==SampleLoop.Backward?-1:1,position=sample.loop==SampleLoop.Backward?sample.loopEnd-1:0,step=SafeInitialStep((clip.frequency/(double)state.sampleRate),((zone.tracking?note-zone.baseNote:0)+sample.tune)/12d),commandGain=1,commandTremolo=1,retriggerGain=1,regionStart=sample.regionStart,regionEnd=sample.regionEnd,selectedStart=sample.regionStart};
                if(sample.legacySamplePitch&&zone.tracking){
                    // MSVC /O2 /fp:fast folds native MidiToFreq(note)/MidiToFreq(base)*fineMul
                    // into powf(2, noteExponent-baseExponent+fineExponent), multiplied by
                    // float(440*float(1/440)) = 0x3f7fffff. Kit's identical note/base folds to 1.
                    float ne=((zone.tracking?note:zone.baseNote)+sample.legacyTranspose-69)*(1f/12f),be=(zone.baseNote-69)*(1f/12f),fe=sample.legacyFineTuneCents*(1f/1200f);
                    double exponent=(ne-be)+fe;double rate=clip.frequency/(double)state.sampleRate;voice.step=math.abs(exponent)<19?(math.pow(2f,(float)exponent)*math.asfloat(0x3f7fffff))*rate:SafeInitialStep(rate,exponent);
                }
                InitTone(ref voice,in tone,in sample,member,col.hasPreviousNote?col.previousNote:note,previousHeld);
                double sourceExponent=tone.kind==0?math.log2(clip.frequency/(double)state.sampleRate)+((zone.tracking?note-zone.baseNote:0)+(double)sample.tune)/12d:math.log2(440d/state.sampleRate)+(note-69)/12d+((double)tone.baseGlobalTune+voice.memberSpread*voice.detuneCurrent)/1200d;
                if(math.isfinite(sourceExponent)&&(sourceExponent<-20||sourceExponent>20))ReportPitchLimit(ref voice);
                voice.filterCutoff=-1;voice.filterQ=-1;state.voices[index]=voice;
                for(int m=0;m<state.modStride;m++)state.modulationState[index*state.modStride+m]=default;
                }
            }
            col.previousNote=note;col.hasPreviousNote=true;state.columns[ci]=col;
            Emit(TrackerEventKind.NoteOn,track,column,note,instrument);
        }
        int ChooseVoice(long newCohort=0)
        {
            for(int i=0;i<state.voices.Length;i++)if(!state.voices[i].active)return i;
            int best=-1;float quiet=float.MaxValue;
            for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.cohort!=newCohort&&v.released&&v.envelope<quiet){best=i;quiet=v.envelope;}}
            if(best<0){quiet=float.MaxValue;for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.cohort!=newCohort&&v.amplitude<quiet){best=i;quiet=v.amplitude;}}}
            var old=state.voices[best];Emit(TrackerEventKind.VoiceStolen,old.track,old.column,old.note,old.instrument);
            for(int i=0;i<state.voices.Length;i++)if(state.voices[i].active&&state.voices[i].cohort==old.cohort){var v=state.voices[i];v.active=false;state.voices[i]=v;}
            return best;
        }
        public void Render(int frames)
        {
            state.ticket[3]=1;MarkManagedRender();
            SapRenderTicket.Enter(state.ticket);AudioThreadGuard.CountBlock();
            for(int t=0;t<state.trackCount;t++)for(int i=0;i<frames;i++){int a=t*state.maxFrames+i;state.left[a]=state.right[a]=0;}
            for(int b=0;b<state.busCount;b++)for(int i=0;i<frames;i++){int a=b*state.maxFrames+i;state.busLeft[a]=state.busRight[a]=0;}
            int offset=0;
            if(state.paused){for(int f=0;f<frames;f++)state.outputLeft[f]=state.outputRight[f]=0;SapRenderTicket.Exit(state.ticket,frames,false);return;}
            while(offset<frames){
                if(state.playing&&state.rowPending)EnterRow();
                if(state.playing){
                    double exactNext=state.tick==state.ticksPerLine-1?state.rowEnd:state.tickDeadline;long next=state.transportOrigin+Quantize(exactNext);
                    if(state.samplePosition>=next){if(state.automationDeadline<exactNext&&state.transportOrigin+Quantize(state.automationDeadline)<=state.samplePosition){ApplyDue(state.automationDeadline);ApplyAutomationPoint();continue;}ApplyDue(exactNext);AdvanceTick();if(state.automationDeadline==exactNext){ApplyAutomationPoint();}continue;}
                    ApplyDue(double.PositiveInfinity);
                    if(state.automationDeadline<exactNext&&state.transportOrigin+Quantize(state.automationDeadline)<=state.samplePosition){ApplyAutomationPoint();continue;}
                    int n=(int)math.min(frames-offset,next-state.samplePosition);
                    for(int ci=0;ci<state.columnCount;ci++){var c=state.columns[ci];if(c.pendingCell>=0&&c.due>state.samplePosition)n=(int)math.min(n,c.due-state.samplePosition);}
                    if(state.automationDeadline<exactNext)n=(int)math.min(n,state.transportOrigin+Quantize(state.automationDeadline)-state.samplePosition);
                    RenderVoices(offset,n);Mix(offset,n);state.samplePosition+=n;offset+=n;
                }else {RenderVoices(offset,frames-offset);Mix(offset,frames-offset);offset=frames;}
            }
            state.ticket[4]++;SapRenderTicket.Exit(state.ticket,frames,false);
        }
        [BurstDiscard] void MarkManagedRender(){state.ticket[3]=-1;}
        void ApplyDue(double before)
        {
            // Distinct fractional deadlines can share Q's frame; retain their exact order before column ties.
            while(true){int best=-1;double deadline=before;for(int ci=0;ci<state.columnCount;ci++){var c=state.columns[ci];if(c.pendingCell>=0&&c.due<=state.samplePosition&&c.dueExact<=before&&(best<0||c.dueExact<deadline)){best=ci;deadline=c.dueExact;}}if(best<0)return;var cell=state.cells[state.columns[best].pendingCell];ApplyCell(in cell);}
        }
        void RenderVoices(int offset,int count)
        {
            EvaluateParameters();
            for(int t=0;t<state.trackCount;t++){byte mute=Muted(t)?(byte)1:(byte)0;for(int f=0;f<count;f++)state.outputMutes[t*state.maxFrames+offset+f]=mute;}
            int active=0;for(int i=0;i<state.voices.Length;i++)if(state.voices[i].active){var voice=state.voices[i];bool follower=false;if(state.tones[voice.sample].members>1)for(int j=0;j<i;j++)if(state.voices[j].active&&state.voices[j].cohort==voice.cohort&&state.voices[j].sample==voice.sample){follower=true;break;}if(!follower)active++;}
            float target=state.legacyMix&&active>1?1/math.sqrt((float)active):1;float scale=state.normalization,scaleStep=(target-scale)/count;
            for(int i=0;i<state.voices.Length;i++){
                var v=state.voices[i];if(!v.active)continue;var s=state.samples[v.sample];var tone=state.tones[v.sample];var clip=s.pcm>=0?state.clips[s.pcm]:default;
                if(Written(v.instrument,TrackerParameter.Attack))s.attack=VoiceParameter(in v,TrackerParameter.Attack);
                if(Written(v.instrument,TrackerParameter.Decay))s.decay=VoiceParameter(in v,TrackerParameter.Decay);
                if(Written(v.instrument,TrackerParameter.Sustain))s.sustain=VoiceParameter(in v,TrackerParameter.Sustain);
                if(Written(v.instrument,TrackerParameter.Release))s.release=VoiceParameter(in v,TrackerParameter.Release);
                if(Written(v.instrument,TrackerParameter.FilterCutoff))s.cutoff=VoiceParameter(in v,TrackerParameter.FilterCutoff);
                if(Written(v.instrument,TrackerParameter.FilterResonance))s.resonance=VoiceParameter(in v,TrackerParameter.FilterResonance);
                for(int f=0;f<count;f++){
                    if(!v.active)break;Envelope(ref v,in s);if(!v.active)break;
                    float volume=1,pan=0,pitch=0,cutoff=s.cutoff,resonance=s.resonance,drive=1;
                    volume=s.orderedVolume?1:v.envelope;EvaluateMods(i,ref v,in s,ref volume,ref pan,ref pitch,ref cutoff,ref resonance,ref drive);
                    ToneTargets(ref v,in tone);
                    double increment=ModulatedStep(ref v,in tone,pitch+(tone.kind==0?v.commandPitch+v.commandArp+v.commandVibrato:0));
                    if(!math.isfinite(increment)||increment<=0){v.active=false;Emit(TrackerEventKind.Diagnostic,v.track,v.column,v.note,v.instrument,(int)TrackerRuntimeDiagnostic.InvalidPitch);break;}
                    float l,r;
                    if(tone.kind!=0){l=tone.kind==2?FM(ref v,in tone,(float)increment):Synth(ref v,in tone,(float)increment);r=l;}
                    else {s.regionStart=v.regionStart;s.regionEnd=v.regionEnd;l=Pcm(in clip,in s,v.position,0,v.released);r=Pcm(in clip,in s,v.position,clip.channels==2?1:0,v.released);if(tone.pcmB>=0)Paired(ref v,in tone,in s,l,r,ref l,ref r);}
                    float globalVolume=VoiceParameter(in v,TrackerParameter.Volume);
                    float sampleVolume=Written(v.instrument,TrackerParameter.Volume)?tone.localVolume*globalVolume:s.volume;
                    float amplitude=sampleVolume*v.memberGain*v.volume*volume*state.tracks[v.track].instrumentGain*(tone.kind==0?v.commandGain*v.commandTremolo:v.retriggerGain);
                    if(v.amplitudeDepth!=0){v.amplitudePhase+=v.amplitudeRate/state.sampleRate;v.amplitudePhase-=math.floor(v.amplitudePhase);amplitude*=math.max(0,1+math.sin(v.amplitudePhase*2*math.PI)*v.amplitudeDepth);}v.amplitude=amplitude;
                    float samplePan=Written(v.instrument,TrackerParameter.Pan)?s.legacyPan?CombinePan(VoiceParameter(in v,TrackerParameter.Pan),tone.localPan):math.clamp(VoiceParameter(in v,TrackerParameter.Pan)+tone.localPan,-1,1):s.pan;
                    samplePan=math.clamp(samplePan+v.memberSpread*VoiceParameter(in v,TrackerParameter.UnisonSpread),-1,1);
                    float p=s.legacyPan?CombinePan(samplePan,math.clamp(v.pan+pan+v.commandPan,-1,1)):math.clamp(samplePan+v.pan+pan+v.commandPan,-1,1);
                    float gainL=s.legacyPan?math.sqrt((1-p)*.5f):clip.channels==2?(p>0?1-p:1):math.cos((p+1)*math.PI*.25f),gainR=s.legacyPan?math.sqrt((1+p)*.5f):clip.channels==2?(p<0?1+p:1):math.sin((p+1)*math.PI*.25f);
                    l*=amplitude*gainL;r*=amplitude*gainR;
                    if(drive!=1){l=math.tanh(l*drive);r=math.tanh(r*drive);}
                    if(s.filterType!=0)Filter(ref v,in s,ref l,ref r,cutoff,resonance);
                    int at=(v.bus>=0?v.bus:v.track)*state.maxFrames+offset+f;
                    if(v.bus>=0){state.busLeft[at]+=l;state.busRight[at]+=r;}else{state.left[at]+=l;state.right[at]+=r;}
                    if(s.delayDestination>=0&&!Muted(v.track)){int dest=s.delayDestination*state.maxFrames+offset+f;state.left[dest]+=l*s.delaySend;state.right[dest]+=r*s.delaySend;}
                    if(s.reverbDestination>=0&&!Muted(v.track)){int dest=s.reverbDestination*state.maxFrames+offset+f;state.left[dest]+=l*s.reverbSend;state.right[dest]+=r*s.reverbSend;}
                    double h=math.log2(increment);if(!math.isfinite(h)){v.active=false;Emit(TrackerEventKind.Diagnostic,v.track,v.column,v.note,v.instrument,(int)TrackerRuntimeDiagnostic.InvalidPitch);break;}if(h<-20||h>20)ReportPitchLimit(ref v);v.age++;
                    if(tone.kind==0){double previous=v.position;v.position+=math.pow(2d,math.clamp(h,-20,20))*v.direction;AdvancePcm(ref v,in s,in clip);AdvanceB(ref v,in tone,in s,previous);}
                }
                state.voices[i]=v;
            }
            // Preserve native voice-slice normalization, independently of chain-control partitions.
            for(int f=0;f<count;f++){scale+=scaleStep;for(int t=0;t<state.trackCount;t++){int a=t*state.maxFrames+offset+f;state.left[a]*=scale;state.right[a]*=scale;}for(int b=0;b<state.busCount;b++){int a=b*state.maxFrames+offset+f;state.busLeft[a]*=scale;state.busRight[a]*=scale;}}
            state.normalization=target;
        }
        void Envelope(ref TrackerVoice v,in TrackerSample s)
        {
            switch(v.stage){
                case 0:v.envelope+=s.attack>0?1/(s.attack*state.sampleRate):1;if(v.envelope>=1){v.envelope=1;v.stage=s.hold>0?1:2;}break;
                case 1:if(v.envelopePosition>=s.attack+s.hold)v.stage=2;break;
                case 2:v.envelope-=s.decay>0?(1-s.sustain)/(s.decay*state.sampleRate):1;if(v.envelope<=s.sustain){v.envelope=s.sustain;v.stage=3;}break;
                case 4:v.envelope-=v.releaseStep;if(v.envelope<=0){v.envelope=0;v.active=false;}break;
            }
            v.envelopePosition+=1d/state.sampleRate;
        }
        static float CombinePan(float a,float b){float r=(a+1)*.5f;r=b<0?r*(1+b):r+(1-r)*b;return r*2-1;}
        float Read(in TrackerPcm clip,int frame,int channel)
        {
            frame=math.clamp(frame,0,clip.frames-1);return state.pcm[clip.offset+frame*clip.channels+channel];
        }
        float ReadRegion(in TrackerPcm clip,in TrackerSample sample,int frame,int channel,double position,bool released)
        {
            if(!sample.legacyPan&&sample.loop!=SampleLoop.Off&&!(released&&sample.releaseExitsLoop)&&position>=sample.loopStart&&position<sample.loopEnd){
                int width=sample.loopEnd-sample.loopStart;
                if(sample.loop==SampleLoop.PingPong){int span=width-1,phase=(frame-sample.loopStart)%(2*span);if(phase<0)phase+=2*span;frame=sample.loopStart+(phase<=span?phase:2*span-phase);}
                else {int phase=(frame-sample.loopStart)%width;if(phase<0)phase+=width;frame=sample.loopStart+phase;}
            }
            return Read(in clip,math.clamp(frame,sample.regionStart, sample.regionEnd>0?sample.regionEnd-1:clip.frames-1),channel);
        }
        float Pcm(in TrackerPcm clip,in TrackerSample sample,double position,int channel,bool released)
        {
            int a=(int)math.floor(position);float t=(float)(position-a);float x=ReadRegion(in clip,in sample,a,channel,position,released),y=ReadRegion(in clip,in sample,a+1,channel,position,released);
            if(sample.interpolation==SampleInterpolation.Linear)return x+(y-x)*t;
            float p=ReadRegion(in clip,in sample,a-1,channel,position,released),q=ReadRegion(in clip,in sample,a+2,channel,position,released);return x+.5f*t*(y-p+t*(2*p-5*x+4*y-q+t*(3*(x-y)+q-p)));
        }
        void AdvancePcm(ref TrackerVoice v,in TrackerSample s,in TrackerPcm clip)
        {
            bool loop=s.loop!=SampleLoop.Off&&!(v.released&&s.releaseExitsLoop)&&s.loopStart>=s.regionStart&&(s.regionEnd==0||s.loopEnd<=s.regionEnd);double start=s.loopStart,end=s.loopEnd,width=end-start;
            if(loop){
                if((s.loop==SampleLoop.Forward||s.loop==SampleLoop.Backward)&&((v.direction>0&&v.position>=end)||(v.direction<0&&v.position<start))){double phase=(v.position-start)%width;if(phase<0)phase+=width;v.position=start+phase;}
            else if(s.loop==SampleLoop.PingPong){double hi=end-1,span=hi-start;if((v.direction>0&&v.position>hi)||(v.direction<0&&v.position<start)){double phase=(v.direction>0?v.position-start:2*span-(v.position-start))%(2*span);if(phase<0)phase+=2*span;v.position=phase<=span?start+phase:hi-(phase-span);v.direction=phase<span?1:-1;}}
            }
            if(v.position<s.regionStart||v.position>=(s.regionEnd>0?s.regionEnd:clip.frames))v.active=false;
        }
        void EvaluateMods(int index,ref TrackerVoice v,in TrackerSample s,ref float volume,ref float pan,ref float pitch,ref float cutoff,ref float resonance,ref float drive)
        {
            for(int i=0;i<s.modCount;i++){
                var m=state.mods[s.modStart+i];int si=index*state.modStride+i;var ms=state.modulationState[si];float value=ms.output;double time=ms.position;
                switch(m.kind){
                    case ModulationDeviceKind.AHDSR:
                        if(m.primaryEnvelope){value=v.envelope;break;}
                        if(ms.finished)break;
                        if(v.released){if(!ms.released){ms.released=true;ms.releaseStart=ms.output;if(!ms.seeked)ms.position=time=0;}value=m.release>0?ms.releaseStart*math.max(0,1-(float)(time/m.release)):0;if(time>=m.release)ms.finished=true;}
                        else if(time<m.attack)value=m.attack>0?(float)time/m.attack:1;
                        else if(time<m.attack+m.hold)value=1;
                        else if(time<m.attack+m.hold+m.decay)value=1-(1-m.sustain)*(float)((time-m.attack-m.hold)/math.max(1e-9f,m.decay));
                        else value=m.sustain;ms.position+=1d/state.sampleRate;ms.seeked=false;break;
                    case ModulationDeviceKind.Multipoint:
                        if(ms.finished)break;
                        if(ms.direction==0){ms.direction=m.loop==SampleLoop.Backward?-1:1;if(!ms.seeked&&m.loop==SampleLoop.Backward&&m.loopEnabled)ms.position=m.loopEnd-1d/state.sampleRate;}
                        if(m.advanceFirst)ms.position+=1d/state.sampleRate;
                        double pos=ms.position;
                        if(!v.released&&m.sustainEnabled)pos=math.min(pos,m.sustainPosition);
                        if(!v.released&&m.loopEnabled){double width=m.loopEnd-m.loopStart;
                            if(m.loop==SampleLoop.Forward&&pos>=m.loopEnd)pos=m.loopStart+(pos-m.loopStart)%width;
                            else if(m.loop==SampleLoop.Backward&&pos<m.loopStart){double loopPhase=(pos-m.loopStart)%width;if(loopPhase<0)loopPhase+=width;pos=m.loopStart+loopPhase;}
                            else if(m.loop==SampleLoop.PingPong&&((ms.direction>0&&pos>m.loopEnd)||(ms.direction<0&&pos<m.loopStart))){double loopPhase=(ms.direction>0?pos-m.loopStart:2*width-(pos-m.loopStart))%(2*width);if(loopPhase<0)loopPhase+=2*width;pos=loopPhase<=width?m.loopStart+loopPhase:m.loopEnd-(loopPhase-width);ms.direction=loopPhase<width?1:-1;}
                        }
                        value=Curve(in m,pos);ms.position=pos+(m.advanceFirst?0:1d/state.sampleRate*(v.released?1:ms.direction));
                        if(!v.released&&m.sustainEnabled)ms.position=math.min(ms.position,m.sustainPosition);if(!m.loopEnabled&&!m.sustainEnabled&&m.pointCount>0&&pos>=state.points[m.points+m.pointCount-1].time)ms.finished=true;ms.seeked=false;break;
                    case ModulationDeviceKind.LFO:
                        if(s.legacyPan&&state.tones[v.sample].kind==0&&m.target==ModulationTarget.Pitch&&m.advanceFirst&&(Written(v.instrument,TrackerParameter.VibratoDepth)||Written(v.instrument,TrackerParameter.VibratoRate)||Written(v.instrument,TrackerParameter.VibratoFadeIn)))continue;
                        uint seed=(uint)(v.cohort*2654435761L+v.note*2246822519L+i*3266489917L);seed^=seed>>16;seed*=0x7feb352du;seed^=seed>>15;
                        float random=(seed&0xffffff)/8388608f-1;
                        double lfoTime=(v.age+(m.advanceFirst?1:0))/(double)state.sampleRate;
                        float phase=(float)(lfoTime*m.rate*(1+random*m.randomness)+m.phase);phase-=math.floor(phase);float wave=m.shape==1?1-4*math.abs(phase-.5f):m.shape==2?2*phase-1:m.shape==3?(phase<.5f?1:-1):math.sin(phase*2*math.PI);value=wave*m.depth*(m.fadeSeconds>0?math.saturate((float)lfoTime/m.fadeSeconds):1);break;
                    case ModulationDeviceKind.Velocity:value=math.lerp(m.min,m.max,math.pow(v.velocity/127f,m.curve));break;
                    case ModulationDeviceKind.KeyTracking:value=math.lerp(m.min,m.max,math.pow(v.note/119f,m.curve));break;
                    case ModulationDeviceKind.Fader:if(m.fadeSeconds>0){if(!ms.finished){value=math.lerp(m.min,m.max,math.saturate((float)(ms.position/m.fadeSeconds)));ms.position+=1d/state.sampleRate;if(ms.position>=m.fadeSeconds)ms.finished=true;}}else value=m.depth;break;
                }
                ms.output=value;state.modulationState[si]=ms;
                switch(m.target){case ModulationTarget.Volume:volume=Combine(volume,value,m.operation);break;case ModulationTarget.Pan:pan=Combine(pan,value,m.operation);break;case ModulationTarget.Pitch:pitch=Combine(pitch,value,m.operation);break;case ModulationTarget.Cutoff:cutoff=Combine(cutoff,value,m.operation);break;case ModulationTarget.Resonance:resonance=Combine(resonance,value,m.operation);break;case ModulationTarget.Drive:drive=Combine(drive,value,m.operation);break;}
            }
            volume=math.clamp(volume,0,16);cutoff=math.clamp(cutoff,20,s.legacyFilter!=0?state.sampleRate*.5f-100:state.sampleRate*.45f);resonance=math.clamp(resonance,.1f,10);drive=math.clamp(drive,0,100);
        }
        float Curve(in TrackerMod m,double pos)
        {
            if(m.pointCount==0)return 1;if(pos<=state.points[m.points].time)return state.points[m.points].value;
            for(int p=1;p<m.pointCount;p++){var a=state.points[m.points+p-1];var b=state.points[m.points+p];if(pos<b.time){float t=(float)((pos-a.time)/(b.time-a.time));return math.lerp(a.value,b.value,math.pow(t,math.max(.00001f,b.exponent)));}}
            return state.points[m.points+m.pointCount-1].value;
        }
        static float Combine(float a,float b,ModulationOperation op)=>op==ModulationOperation.Add?a+b:op==ModulationOperation.Replace?b:a*b;
        void Filter(ref TrackerVoice v,in TrackerSample s,ref float l,ref float r,float cutoff,float q)
        {
            if(v.filterCutoff!=cutoff||v.filterQ!=q){float w=2*math.PI*math.clamp(cutoff,20,s.legacyFilter!=0?state.sampleRate*.5f-100:state.sampleRate*.45f)/state.sampleRate;float cos=math.cos(w),alpha=math.sin(w)/(2*q),inv=1/(1+alpha);
                if(s.filterType==2){v.b0=(1+cos)*.5f*inv;v.b1=-(1+cos)*inv;v.b2=v.b0;}
                else if(s.filterType==3){v.b0=alpha*inv;v.b1=0;v.b2=-alpha*inv;}
                else{v.b0=(1-cos)*.5f*inv;v.b1=(1-cos)*inv;v.b2=v.b0;}
                v.a1=-2*cos*inv;v.a2=(1-alpha)*inv;v.filterCutoff=cutoff;v.filterQ=q;
            }
            float outL=v.b0*l+v.b1*v.xL1+v.b2*v.xL2-v.a1*v.fL1-v.a2*v.fL2;v.xL2=v.xL1;v.xL1=l;v.fL2=v.fL1;v.fL1=outL;l=outL;
            float outR=v.b0*r+v.b1*v.xR1+v.b2*v.xR2-v.a1*v.fR1-v.a2*v.fR2;v.xR2=v.xR1;v.xR1=r;v.fR2=v.fR1;v.fR1=outR;r=outR;
        }
        void Mix(int offset,int frames)
        {
            var context=new ChainProcessContext{effects=new VoiceContext{sampleRate=state.sampleRate,sourceDuration=30,sourcePeak=1},followSource=false};
            for(int b=0;b<state.busCount;b++){var route=state.buses[b];ref var chain=ref state.chains[route.chain];int at=b*state.maxFrames+offset;chain.processor.Process(in chain.layout,state.busLeft,state.busRight,at,frames,in context);int dest=route.track*state.maxFrames+offset;for(int f=0;f<frames;f++){state.left[dest+f]+=state.busLeft[at+f];state.right[dest+f]+=state.busRight[at+f];}}
            for(int oi=0;oi<state.trackCount;oi++){
                int ti=state.order[oi];var t=state.tracks[ti];int at=ti*state.maxFrames+offset;
                for(int f=0;f<frames;f++){if(t.gainRemaining>0){t.livePostGain+=t.gainStep;if(--t.gainRemaining==0)t.livePostGain=t.postGain;}if(t.panRemaining>0){t.livePostPan+=t.panStep;if(--t.panRemaining==0)t.livePostPan=t.postPan;}state.faderGain[at+f]=t.livePostGain;state.faderPan[at+f]=t.livePostPan;}state.tracks[ti]=t;
                for(int f=0;f<frames;f++){float l=state.left[at+f],r=state.right[at+f];if(t.kind==TrackKind.Master&&state.legacyMix){l=Knee(l);r=Knee(r);}float mid=(l+r)*.5f,side=(l-r)*.5f*t.width;l=mid+side;r=mid-side;PanGain(ref l,ref r,t.preGain,t.prePan);state.left[at+f]=l;state.right[at+f]=r;}
                SendAt(ti,0,at,frames);
                for(int c=0;c<t.chainCount;c++){ref var chain=ref state.chains[t.chainStart+c];chain.processor.Process(in chain.layout,state.left,state.right,at,frames,in context);SendAt(ti,chain.position,at,frames);}
                for(int f=0;f<frames;f++){float l=state.left[at+f],r=state.right[at+f];PanGain(ref l,ref r,state.faderGain[at+f],state.faderPan[at+f]);if(state.outputMutes[at+f]!=0)l=r=0;state.left[at+f]=l;state.right[at+f]=r;if(t.output>=0){int dest=t.output*state.maxFrames+offset+f;state.left[dest]+=l;state.right[dest]+=r;}}
            }
            int master=state.master*state.maxFrames+offset;for(int f=0;f<frames;f++){state.outputLeft[offset+f]=state.left[master+f];state.outputRight[offset+f]=state.right[master+f];}
        }
        void SendAt(int track,int position,int at,int frames)
        {
            var t=state.tracks[track];for(int s=0;s<t.sendCount;s++){var send=state.sends[t.sendStart+s];if(send.position!=position)continue;for(int f=0;f<frames;f++){if(state.outputMutes[at+f]!=0)continue;float l=state.left[at+f],r=state.right[at+f];if(send.postfader)PanGain(ref l,ref r,state.faderGain[at+f],state.faderPan[at+f]);int dest=send.destination*state.maxFrames+(at%state.maxFrames)+f;state.left[dest]+=l*send.gain;state.right[dest]+=r*send.gain;}}
        }
        static void PanGain(ref float l,ref float r,float gain,float pan){l*=gain*(pan>0?1-pan:1);r*=gain*(pan<0?1+pan:1);}
        static float Knee(float x)=>x>.9f?.9f+.1f*math.tanh((x-.9f)/.1f):x<-.9f?-.9f-.1f*math.tanh((-x-.9f)/.1f):x;
    }
}
