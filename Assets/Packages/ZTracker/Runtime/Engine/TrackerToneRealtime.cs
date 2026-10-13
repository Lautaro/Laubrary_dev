using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public unsafe partial struct TrackerRealtime
    {
        float Parameter(int instrument,TrackerParameter p)=>state.parameterLive[instrument*TrackerParameters.Stride+(int)p];
        float VoiceParameter(in TrackerVoice voice,TrackerParameter parameter)
        {
            int preset=state.tones[voice.sample].parameterSet;
            float value=preset>=0&&!Written(voice.instrument,parameter)?state.presetParameters[preset*TrackerParameters.Stride+(int)parameter]:Parameter(voice.instrument,parameter);
            var tone=state.tones[voice.sample];int slot=tone.parameterEnvelopeSlots[(int)parameter];
            if(slot!=0)value=state.modulationState[voice.modulationOffset+slot-1].output;
            return TrackerParameters.Clamp(parameter,value);
        }
        bool VoiceParameterActive(in TrackerVoice voice,TrackerParameter parameter)
        {
            if(Written(voice.instrument,parameter))return true;
            return HasParameterEnvelope(in voice,parameter);
        }
        bool HasParameterEnvelope(in TrackerVoice voice,TrackerParameter parameter)
        {
            var tone=state.tones[voice.sample];return tone.parameterEnvelopeSlots[(int)parameter]!=0;
        }
        bool Written(int instrument,TrackerParameter p)=>state.parameterWritten[instrument*TrackerParameters.Stride+(int)p]!=0;
        void ResetToneDefaults()
        {
            for(int i=0;i<state.instrumentCount*TrackerParameters.Stride;i++){state.parameterDirect[i]=state.parameterBase[i];state.parameterWritten[i]=0;}
            for(int i=0;i<state.instrumentCount*8;i++){var m=state.macros[i];m.value=m.target=m.authored;m.sliding=false;m.step=0;state.macros[i]=m;}EvaluateParameters();
        }
        void ApplyParameterCommand(in TrackerCommand c)
        {
            if(!math.isfinite(c.value))return;
            switch(c.kind){
                case TrackerCommandKind.MacroSet:case TrackerCommandKind.MacroTarget:
                    if(c.a<0||c.a>=state.instrumentCount||c.b<0||c.b>=8)return;
                    int at=c.a*8+c.b;var m=state.macros[at];m.target=math.saturate(c.value);
                    if(c.kind==TrackerCommandKind.MacroSet){m.value=m.target;m.sliding=false;}else {m.step=math.asfloat((int)c.generation);if(!math.isfinite(m.step)||m.step<=0)return;m.step=math.min(m.step,1);m.sliding=true;}state.macros[at]=m;break;
                case TrackerCommandKind.MacroAdvance:AdvanceMacroTicks(c.a);break;
                case TrackerCommandKind.ParameterSet:
                    if(c.a<0||c.a>=state.instrumentCount||c.b<0||c.b>=TrackerParameters.Stride)return;
                    int p=c.a*TrackerParameters.Stride+c.b;state.parameterDirect[p]=TrackerParameters.Clamp((TrackerParameter)c.b,c.value);state.parameterWritten[p]|=1;break;
                case TrackerCommandKind.ExternalSet:
                    if(c.a<0||c.a>=state.externalRouteCount)return;var route=state.externalRoutes[c.a];float value=RouteValue(in route,math.saturate(c.value));
                    if(route.parameter<0){int mi=route.instrument*8-1-route.parameter;var macro=state.macros[mi];macro.value=macro.target=math.saturate(value);macro.sliding=false;state.macros[mi]=macro;}
                    else {int pi=route.instrument*TrackerParameters.Stride+route.parameter;state.parameterDirect[pi]=TrackerParameters.Clamp((TrackerParameter)route.parameter,value);state.parameterWritten[pi]|=1;}break;
                case TrackerCommandKind.AmplitudeModifier:
                    if(c.a<0||c.a>=state.trackCount||c.b<0||c.b>=state.tracks[c.a].columnCount)return;float hz=math.asfloat((int)c.generation);if(!math.isfinite(hz)||hz<0||hz>1000||c.value<0||c.value>16)return;
                    long cohort=state.columns[state.tracks[c.a].columns+c.b].cohort;
                    for(int i=0;i<state.voices.Length;i++){var v=state.voices[i];if(v.active&&v.cohort==cohort){v.amplitudeDepth=c.value;v.amplitudeRate=hz;v.amplitudePhase=0;state.voices[i]=v;}}break;
            }
            EvaluateParameters();
        }
        void AdvanceMacroTicks(int instrument)
        {
            for(int ii=0;ii<state.instrumentCount;ii++){
                if(instrument>=0&&ii!=instrument)continue;bool foreground=false;
                for(int v=0;v<state.voices.Length;v++){var voice=state.voices[v];if(voice.active&&!voice.released&&voice.instrument==ii&&voice.cohort==state.columns[state.tracks[voice.track].columns+voice.column].cohort){foreground=true;break;}}
                if(!foreground)continue;
                for(int m=0;m<8;m++){int at=ii*8+m;var value=state.macros[at];if(!value.sliding)continue;value.value+=math.clamp(value.target-value.value,-value.step,value.step);if(value.value==value.target)value.sliding=false;state.macros[at]=value;}
            }
        }
        void EvaluateParameters()
        {
            for(int p=0;p<state.instrumentCount*TrackerParameters.Stride;p++){state.parameterLive[p]=state.parameterDirect[p];state.parameterWritten[p]&=1;}
            // Compilation emits macro index order and authored mapping-list order. Every valid write wins over its predecessor.
            for(int i=0;i<state.macroRouteCount;i++){var route=state.macroRoutes[i];int p=route.instrument*TrackerParameters.Stride+route.parameter;state.parameterLive[p]=TrackerParameters.Clamp((TrackerParameter)route.parameter,RouteValue(in route,state.macros[route.macro].value));state.parameterWritten[p]|=2;}
        }
        float RouteValue(in TrackerParameterRoute route,float value)
        {
            if(route.pointCount>0)value=PointCurve(route.points,route.pointCount,value,false);
            // Finite opposite-sign float endpoints can overflow a float subtraction.
            float result=(float)((double)route.min+((double)route.max-route.min)*value);
            if(route.quantum>0)result=route.lower+route.quantum*math.floor((result-route.lower)/route.quantum+.5f);
            return math.clamp(result,math.min(route.min,route.max),math.max(route.min,route.max));
        }
        float PointCurve(int start,int count,float time,bool exponent)
        {
            if(count==0)return 0;var first=state.points[start];if(time<=(float)first.time)return first.value;
            for(int n=1;n<count;n++){var b=state.points[start+n];if(time<=(float)b.time){var a=state.points[start+n-1];float t=(time-(float)a.time)/((float)b.time-(float)a.time);if(exponent)t=math.pow(t,b.exponent);return a.value+(b.value-a.value)*t;}}
            return state.points[start+count-1].value;
        }
        float ToneCurve(in TrackerMod m,in TrackerVoice voice)=>PointCurve(m.points,m.pointCount,(float)ToneCurvePosition(in m,in voice),true);
        /// Where a voice is on one of the synth's own envelopes, in the envelope's own unit (seconds, or song rows for an
        /// envelope measured in rows). The sound and the editor's playheads both read it.
        double ToneCurvePosition(in TrackerMod m,in TrackerVoice voice)
        {
            double time=m.rows?voice.envRows:voice.envTime;
            if(voice.released&&(m.loopEnabled||m.sustainEnabled)){
                double releasedAt=m.rows?voice.releaseRows:voice.releaseAge/(double)state.sampleRate;
                double releasePosition=m.sustainEnabled?math.min(releasedAt,m.sustainPosition):releasedAt;
                if(m.loopEnabled&&releasePosition>m.loopEnd)releasePosition=ToneLoop(in m,releasePosition);
                return releasePosition+math.max(0,time-releasedAt);
            }
            return TonePosition(in m,time);
        }
        /// Song rows per rendered sample at the current tempo: how far an envelope measured in rows moves each sample.
        double RowsPerSample=>state.bpm*state.linesPerBeat/(60d*state.sampleRate);
        /// Where a held note is on one of the synth's own envelopes: held at the sustain point, wrapped into the loop.
        static double TonePosition(in TrackerMod m,double time)
        {
            if(m.sustainEnabled)time=math.min(time,m.sustainPosition);
            return m.loopEnabled&&time>m.loopEnd?ToneLoop(in m,time):time;
        }
        static double ToneLoop(in TrackerMod m,double time)
        {
            double width=m.loopEnd-m.loopStart,phase=(time-m.loopStart)%width;
            if(m.loop==SampleLoop.PingPong&&((long)((time-m.loopStart)/width)&1)!=0)phase=width-phase;
            return m.loopStart+phase;
        }
        // Polyphonic legato (T-0633): while another note of this instrument is held on the track (in any column), a new note
        // takes over that note's running synth state instead of restarting it: the amplitude, blend, pulse, ratio, phase
        // modulation and detune envelopes and their lagging values, the instrument modulation devices, the FM operator
        // envelopes and the vibrato. Only what makes it a different note stays its own: pitch, oscillator phases,
        // velocity and level, pan, glide. The source is the most recently started held voice of the same sample and unison
        // member; a note whose only neighbours are released tails starts normally. Fixed-size copies only.
        void JoinRunningNote(ref TrackerVoice v,int index)
        {
            int best=-1;long bestAge=long.MaxValue;
            for(int i=0;i<state.voices.Length;i++)
            {
                if(i==index)continue;var o=state.voices[i];
                if(!o.active||o.released||o.track!=v.track||o.instrument!=v.instrument||o.sample!=v.sample||o.cohort==v.cohort||o.memberSpread!=v.memberSpread||o.age>=bestAge)continue;
                best=i;bestAge=o.age;
            }
            if(best<0)return;
            var s=state.voices[best];
            v.stage=s.stage;v.envelope=s.envelope;v.envelopePosition=s.envelopePosition;v.envTime=s.envTime;v.envRows=s.envRows;
            v.blendStage=s.blendStage;v.blendLevel=s.blendLevel;v.blendCurrent=s.blendCurrent;v.pmCurrent=s.pmCurrent;v.ratioCurrent=s.ratioCurrent;v.pulseCurrent=s.pulseCurrent;v.detuneCurrent=s.detuneCurrent;
            v.fmStage=s.fmStage;v.fmLevel=s.fmLevel;v.vibratoPhase=s.vibratoPhase;v.vibratoFade=s.vibratoFade;v.vibratoSmooth=s.vibratoSmooth;
            for(int m=0;m<state.modStride;m++)state.modulationState[v.modulationOffset+m]=state.modulationState[s.modulationOffset+m];
        }
        void InitTone(ref TrackerVoice v,in TrackerTone tone,in TrackerSample sample,int member,int previousNote,bool held)
        {
            v.memberSpread=tone.members>1?(member*(1f/(tone.members-1)))*2-1:0;v.memberGain=1/math.sqrt((float)tone.members);
            v.blendCurrent=Written(v.instrument,TrackerParameter.Blend)?VoiceParameter(in v,TrackerParameter.Blend):tone.baseBlend;v.pmCurrent=Written(v.instrument,TrackerParameter.PMDepth)?VoiceParameter(in v,TrackerParameter.PMDepth):tone.basePM;v.ratioCurrent=VoiceParameter(in v,TrackerParameter.WaveBRatio);v.pulseCurrent=VoiceParameter(in v,TrackerParameter.PulseWidth);v.detuneCurrent=state.parameterBase[v.instrument*TrackerParameters.Stride+(int)TrackerParameter.UnisonDetune];
            v.noiseA=Seed((uint)v.cohort,(uint)member,1);v.noiseB=Seed((uint)v.cohort,(uint)member,2);v.directionB=tone.loopB==SampleLoop.Backward?-1:1;v.positionB=tone.loopB==SampleLoop.Backward?tone.loopEndB-1:0;
            v.vibratoSmooth=v.arpSmooth=1;v.vibratoFade=VoiceParameter(in v,TrackerParameter.VibratoFadeIn)>0?0:1;
            if(tone.kind!=0){double cents=tone.baseGlobalTune+tone.transposeCents+(double)v.memberSpread*v.detuneCurrent;if(math.abs(cents)<12000){float freq=440*math.pow(2f,(v.note-69)*(1f/12f));float fine=math.pow(2f,(float)cents*(1f/1200f));v.step=math.asfloat(math.asint(freq*fine))/(double)state.sampleRate;}else v.step=SafeInitialStep(440d/state.sampleRate,(v.note-69)/12d+cents/1200d);}
            if(tone.pcmB>=0){var clip=state.clips[tone.pcmB];float ne=(v.note-69)*(1f/12f),be=(tone.baseNoteB-69)*(1f/12f),fe=tone.fineTuneB*(1f/1200f);double boundedB=SafeInitialStep(clip.frequency/(double)state.sampleRate,(v.note-tone.baseNoteB+(double)tone.fineTuneB/100)/12d);v.stepB=math.abs(tone.fineTuneB)>12000?boundedB:sample.legacyPan?(math.pow(2f,(ne-be)+fe)*math.asfloat(0x3f7fffff))*(clip.frequency/(double)state.sampleRate):math.pow(2d,(v.note-tone.baseNoteB+tone.fineTuneB/100)/12d)*(clip.frequency/(double)state.sampleRate);}
            if(tone.glide&&(!tone.legato||held)&&previousNote!=v.note)StartGlide(ref v,previousNote-v.note);else {v.glideSemis=0;v.glideCurrent=1;}
            float speed=PointCurve(tone.arpPoints,tone.arpPointCount,0,false);v.arpStep=math.max(1,speed*state.sampleRate/(tone.arpPerNote?1:math.max(1,tone.arpNoteCount)));
            if(tone.arpNoteCount>=2){float first=state.points[tone.arpNotes].value;v.step*=math.pow(2d,first/12d);}
            // A new note starts at the current feedback amount; only later edits glide.
            if(tone.kind==2)v.fmFeedbackAmount=FMFeedbackTarget(in v);
            // Main ADSR remains per voice. Macros alter its rates without touching the saved asset.
        }
        static void StartGlide(ref TrackerVoice v,double semitones)
        {
            v.glideSemis=semitones;v.glideSpan=math.abs(semitones);v.glideCurrent=semitones==0?1:math.exp2(semitones/12d);
        }
        static uint Seed(uint cohort,uint member,uint oscillator){uint s=cohort*0x9e3779b9u+member*0x85ebca6bu+oscillator*0xc2b2ae35u;s^=s>>16;s*=0x7feb352du;s^=s>>15;return s==0?1:s;}
        static float Noise(ref uint rng){rng^=rng<<13;rng^=rng>>17;rng^=rng<<5;return (rng&0xffffff)/8388608f-1;}
        static float Wave(int wave,float phase,float pw,ref uint rng,ref float3 pink)
        {
            switch(wave){case 0:return math.sin(phase*2*math.PI);case 1:return phase<pw?1:-1;case 2:return 2*phase-1;case 3:return 1-2*phase;case 4:return phase<.5f?4*phase-1:3-4*phase;case 5:return Noise(ref rng);case 6:float white=Noise(ref rng);pink.x=.99765f*pink.x+white*.099046f;pink.y=.963f*pink.y+white*.2965164f;pink.z=.57f*pink.z+white*1.0526913f;return (pink.x+pink.y+pink.z+white*.1848f)*.25f;default:return 0;}
        }
        double ModulatedStep(ref TrackerVoice v,in TrackerTone tone,float pitch)
        {
            double step=v.step;int ii=v.instrument;
            // Glide (and a sliding legato) covers its whole distance at a constant rate in semitones, so it arrives in
            // exactly the glide time (T-0627). It used to approach the note exponentially with the glide time as its
            // time constant, which took about three times the stated time to get within 5%.
            if(v.glideSemis!=0){
                double seconds=VoiceParameter(in v,TrackerParameter.GlideSeconds),move=seconds>0?v.glideSpan/(seconds*state.sampleRate):double.MaxValue;
                // A slide shorter than one sample is a jump: this sample already plays the new note.
                if(math.abs(v.glideSemis)<=move&&seconds*state.sampleRate<1){v.glideSemis=0;v.glideCurrent=1;}
                else {step*=v.glideCurrent;if(math.abs(v.glideSemis)<=move){v.glideSemis=0;v.glideCurrent=1;}else {v.glideSemis-=v.glideSemis>0?move:-move;v.glideCurrent=math.exp2(v.glideSemis/12d);}}
            }
            double cadence=state.playing?state.sampleRate*60d/(state.bpm*state.linesPerBeat*state.ticksPerLine):3528;
            if(tone.arpNoteCount>=2){v.arpTime+=1f/state.sampleRate;v.arpCounter++;if(v.arpCounter>=v.arpStep){v.arpCounter-=v.arpStep;v.arpIndex=(v.arpIndex+1)%tone.arpNoteCount;float speed=PointCurve(tone.arpPoints,tone.arpPointCount,v.arpTime,false);v.arpStep=math.max(1,speed*state.sampleRate/(tone.arpPerNote?1:tone.arpNoteCount));}
                float note=state.points[tone.arpNotes+v.arpIndex].value-state.points[tone.arpNotes].value;double target=math.pow(2f,note/12f);v.arpSmooth+=(target-v.arpSmooth)/math.max(1,cadence);step*=v.arpSmooth;
            }
            // Retained sampler vibrato uses P3's modulation device unless explicitly routed live.
            bool vibrato=tone.kind!=0||VoiceParameterActive(in v,TrackerParameter.VibratoDepth)||VoiceParameterActive(in v,TrackerParameter.VibratoRate)||VoiceParameterActive(in v,TrackerParameter.VibratoFadeIn);
            if(vibrato){float depth=VoiceParameter(in v,TrackerParameter.VibratoDepth),rate=VoiceParameter(in v,TrackerParameter.VibratoRate),fade=VoiceParameter(in v,TrackerParameter.VibratoFadeIn);uint seed=Seed((uint)v.cohort,(uint)v.note,3);float jitter=(seed&0xffffff)/8388608f-1;rate*=1+jitter*tone.vibratoRandomness;v.vibratoFade=fade>0?math.min(1,v.vibratoFade+1/(fade*state.sampleRate)):1;v.vibratoPhase+=rate/state.sampleRate*2*math.PI;if(v.vibratoPhase>2*math.PI)v.vibratoPhase-=2*math.PI;double target=math.pow(2f,math.sin(v.vibratoPhase)*depth*v.vibratoFade/1200);v.vibratoSmooth+=(target-v.vibratoSmooth)/math.max(1,cadence);step*=v.vibratoSmooth;}
            float fine=VoiceParameter(in v,TrackerParameter.FineTune)-tone.baseGlobalTune;
            if(tone.kind!=0)fine+=v.memberSpread*(v.detuneCurrent-state.parameterBase[ii*TrackerParameters.Stride+(int)TrackerParameter.UnisonDetune]);
            double exponent=math.log2(step)+pitch/12d+fine/1200d;
            if(!math.isfinite(exponent))return double.NaN;
            if(exponent < -20 || exponent > 20){ReportPitchLimit(ref v);return math.pow(2d,math.clamp(exponent,-20,20));}
            return step*math.pow(2d,pitch/12d+fine/1200d);
        }
        void ReportPitchLimit(ref TrackerVoice voice)
        {
            if(voice.pitchLimited)return;
            bool reported=false;
            for(int i=0;i<state.voices.Length;i++)if(state.voices[i].active&&state.voices[i].cohort==voice.cohort&&state.voices[i].pitchLimited)reported=true;
            voice.pitchLimited=true;
            if(!reported)Emit(TrackerEventKind.Diagnostic,voice.track,voice.column,voice.note,voice.instrument,(int)TrackerRuntimeDiagnostic.PitchLimit);
        }
        static double SafeInitialStep(double rate,double exponent)
        {
            if(!math.isfinite(rate)||rate<=0||!math.isfinite(exponent))return double.NaN;
            double combined=math.log2(rate)+exponent;
            return combined < -20 || combined > 20 ? math.pow(2d,math.clamp(combined,-20,20)) : rate*math.pow(2d,exponent);
        }
        void ToneTargets(ref TrackerVoice v,in TrackerTone tone)
        {
            int ii=v.instrument;float blend=Written(ii,TrackerParameter.Blend)?VoiceParameter(in v,TrackerParameter.Blend):tone.baseBlend,pw=VoiceParameter(in v,TrackerParameter.PulseWidth),ratio=VoiceParameter(in v,TrackerParameter.WaveBRatio),pm=Written(ii,TrackerParameter.PMDepth)?VoiceParameter(in v,TrackerParameter.PMDepth):tone.basePM,detune=VoiceParameter(in v,TrackerParameter.UnisonDetune);
            if(tone.blendEnvelope&&v.blendStage!=5){if(v.released){if(v.blendStage!=4){v.blendStage=4;v.blendReleaseStart=v.blendLevel;}v.blendLevel=math.max(0,v.blendLevel-(tone.blendRelease>0?1/(tone.blendRelease*state.sampleRate):1));if(v.blendLevel<=0)v.blendStage=5;}else if(v.blendStage==0){v.blendLevel+=tone.blendAttack>0?1/(tone.blendAttack*state.sampleRate):1;if(v.blendLevel>=1){v.blendLevel=1;v.blendStage=2;}}else if(v.blendStage==2){v.blendLevel-=tone.blendDecay>0?(1-tone.blendSustain)/(tone.blendDecay*state.sampleRate):1;if(v.blendLevel<=tone.blendSustain){v.blendLevel=tone.blendSustain;v.blendStage=3;}}v.blendCurrent=v.blendLevel;}
            for(int n=0;n<5;n++){var env=state.toneEnvelopes[tone.envelopes+n];if(env.pointCount==0)continue;float value=ToneCurve(in env,in v);switch(n){case 0:blend=value;break;case 1:pw=value;break;case 2:ratio=value;break;case 3:pm=value;break;case 4:detune=value;break;}}
            float smooth=1/(.005f*state.sampleRate);v.blendCurrent+=(blend-v.blendCurrent)*smooth;v.pulseCurrent+=(pw-v.pulseCurrent)*smooth;v.ratioCurrent+=(ratio-v.ratioCurrent)*smooth;v.pmCurrent+=(pm-v.pmCurrent)*smooth;v.detuneCurrent+=(detune-v.detuneCurrent)*smooth;v.envTime+=1d/state.sampleRate;v.envRows+=RowsPerSample;
        }
        float Synth(ref TrackerVoice v,in TrackerTone tone,float increment)
        {
            float a=Wave(tone.waveA,v.phaseA,math.clamp(v.pulseCurrent,.01f,.99f),ref v.noiseA,ref v.pinkA),b;
            float phase=tone.blendMode==3?v.phaseB+a*v.pmCurrent:v.phaseB;phase-=math.floor(phase);b=Wave(tone.waveB,phase,math.clamp(v.pulseCurrent,.01f,.99f),ref v.noiseB,ref v.pinkB);
            if(tone.blendMode==2){float cutoff=.15f+.85f/(1+v.ratioCurrent*.5f);v.syncFilter+=cutoff*(b-v.syncFilter);b=v.syncFilter;}else if(tone.blendMode==1)b*=a;
            float output=tone.blendMode==0?a*(1-v.blendCurrent)+b*v.blendCurrent:b*(1-v.blendCurrent)+a*v.blendCurrent;
            float prev=v.phaseA;v.phaseA+=increment;if(v.phaseA>=1)v.phaseA-=math.floor(v.phaseA);v.phaseB+=increment*v.ratioCurrent;if(v.phaseB>=1)v.phaseB-=math.floor(v.phaseB);if(tone.blendMode==2&&v.phaseA<prev)v.phaseB=0;return output;
        }
        float FM(ref TrackerVoice v,in TrackerTone tone,float increment)
        {
            float4 level=default,inc=default;
            for(int op=0;op<4;op++){
                int p=v.instrument*TrackerParameters.Stride+(int)TrackerParameter.Op0Ratio+op*7;float attack=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+3)),decay=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+4)),sustain=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+5)),release=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+6));
                if(v.released){if(v.fmStage[op]!=4){v.fmReleaseStart[op]=v.fmLevel[op];v.fmStage[op]=4;}v.fmLevel[op]=math.max(0,v.fmLevel[op]-(release>0?v.fmReleaseStart[op]/(release*state.sampleRate):1));}
                else if(v.fmStage[op]==0){v.fmLevel[op]+=attack>0?1/(attack*state.sampleRate):1;if(v.fmLevel[op]>=1){v.fmLevel[op]=1;v.fmStage[op]=2;}}
                else if(v.fmStage[op]==2){v.fmLevel[op]-=decay>0?(1-sustain)/(decay*state.sampleRate):1;if(v.fmLevel[op]<=sustain){v.fmLevel[op]=sustain;v.fmStage[op]=3;}}
                level[op]=v.fmLevel[op]*VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+2));inc[op]=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+1))>0?VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+1))/state.sampleRate:increment*VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride));
            }
            float feedback=VoiceParameter(in v,TrackerParameter.FMFeedback)*v.fmPrevious;
            float a=math.sin(v.fmPhase.x*2*math.PI+feedback)*level.x,b=math.sin(v.fmPhase.y*2*math.PI)*level.y,c=math.sin(v.fmPhase.z*2*math.PI)*level.z,d=math.sin(v.fmPhase.w*2*math.PI)*level.w,output;
            switch(tone.algorithm){
                case 0:b=math.sin((v.fmPhase.y+a)*2*math.PI)*level.y;c=math.sin((v.fmPhase.z+b)*2*math.PI)*level.z;output=math.sin((v.fmPhase.w+c)*2*math.PI)*level.w;break;
                case 1:b=math.sin((v.fmPhase.y+a)*2*math.PI)*level.y;d=math.sin((v.fmPhase.w+c)*2*math.PI)*level.w;output=(b+d)*.5f;break;
                case 2:c=math.sin((v.fmPhase.z+a+b)*2*math.PI)*level.z;output=math.sin((v.fmPhase.w+c)*2*math.PI)*level.w;break;
                case 3:b=math.sin((v.fmPhase.y+a)*2*math.PI)*level.y;output=(b+c+d)/3;break;
                case 4:b=math.sin((v.fmPhase.y+a)*2*math.PI)*level.y;c=math.sin((v.fmPhase.z+b)*2*math.PI)*level.z;output=(c+d)*.5f;break;
                default:a=math.sin((v.fmPhase.x+feedback)*2*math.PI)*level.x;output=(a+b+c+d)*.25f;break;
            }
            v.fmPhase+=inc;v.fmPhase-=math.floor(v.fmPhase);v.fmPrevious=output;return output;
        }
        // A voice playing one of the ten single-carrier setups (fmAlgorithm 101..110). The
        // operator envelope loop is a copy of the one in FM() on purpose: the older routings
        // keep their own function untouched, so their compiled code and output stay
        // bit-identical to before the setups existed.
        float FMSetupVoice(ref TrackerVoice v,in TrackerTone tone,float increment)
        {
            float4 level=default,inc=default;
            for(int op=0;op<4;op++){
                int p=v.instrument*TrackerParameters.Stride+(int)TrackerParameter.Op0Ratio+op*7;float attack=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+3)),decay=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+4)),sustain=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+5)),release=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+6));
                if(v.released){if(v.fmStage[op]!=4){v.fmReleaseStart[op]=v.fmLevel[op];v.fmStage[op]=4;}v.fmLevel[op]=math.max(0,v.fmLevel[op]-(release>0?v.fmReleaseStart[op]/(release*state.sampleRate):1));}
                else if(v.fmStage[op]==0){v.fmLevel[op]+=attack>0?1/(attack*state.sampleRate):1;if(v.fmLevel[op]>=1){v.fmLevel[op]=1;v.fmStage[op]=2;}}
                else if(v.fmStage[op]==2){v.fmLevel[op]-=decay>0?(1-sustain)/(decay*state.sampleRate):1;if(v.fmLevel[op]<=sustain){v.fmLevel[op]=sustain;v.fmStage[op]=3;}}
                level[op]=v.fmLevel[op]*VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+2));inc[op]=VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+1))>0?VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride+1))/state.sampleRate:increment*VoiceParameter(in v,(TrackerParameter)(p%TrackerParameters.Stride));
            }
            float feedback=VoiceParameter(in v,TrackerParameter.FMFeedback)*v.fmPrevious;
            float output=FMSetup(ref v,tone.algorithm-TrackerFmSetups.Base,level,feedback);
            v.fmPhase+=inc;v.fmPhase-=math.floor(v.fmPhase);v.fmPrevious=output;return output;
        }
        float3 FMFeedbackTarget(in TrackerVoice v)=>new float3(VoiceParameter(in v,TrackerParameter.Op0Feedback),VoiceParameter(in v,TrackerParameter.Op1Feedback),VoiceParameter(in v,TrackerParameter.Op2Feedback));
        // One single-carrier setup: modulators A, B, D (slots 0..2) feed carrier C (slot 3)
        // along the setup's routes. Phases accumulate in the same order as the older Stack
        // and Merge code (setups 4 and 7 match them to float rounding). Each modulator's own
        // feedback is added only when its amount is non-zero, so 0 is bit-identical to
        // having no feedback at all.
        float FMSetup(ref TrackerVoice v,int setup,float4 level,float feedback)
        {
            int r=TrackerFmSetups.Routes(setup);
            // A changed amount glides over a few milliseconds (sample-rate independent)
            // rather than stepping, so live edits on a held note do not click.
            float3 target=FMFeedbackTarget(in v),amount=v.fmFeedbackAmount;
            if(math.any(amount!=target)){amount+=(target-amount)*math.min(1f,1f/(TrackerFmSetups.FeedbackGlideSeconds*state.sampleRate));amount=math.select(amount,target,math.abs(target-amount)<1e-6f);v.fmFeedbackAmount=amount;}
            float3 shift=amount*TrackerFmSetups.MaxFeedbackCycles*.5f*(v.fmFeedbackLast+v.fmFeedbackBefore);
            float pa=v.fmPhase.x;if(amount.x!=0)pa+=shift.x;
            float ra=math.sin(pa*2*math.PI+feedback),a=ra*level.x;
            float pb=v.fmPhase.y;if((r&TrackerFmSetups.AtoB)!=0)pb+=a;if(amount.y!=0)pb+=shift.y;
            float rb=math.sin(pb*2*math.PI),b=rb*level.y;
            float pd=v.fmPhase.z;if((r&TrackerFmSetups.AtoD)!=0)pd+=a;if((r&TrackerFmSetups.BtoD)!=0)pd+=b;if(amount.z!=0)pd+=shift.z;
            float rd=math.sin(pd*2*math.PI),d=rd*level.z;
            float pc=v.fmPhase.w;if((r&TrackerFmSetups.AtoC)!=0)pc+=a;if((r&TrackerFmSetups.BtoC)!=0)pc+=b;if((r&TrackerFmSetups.DtoC)!=0)pc+=d;
            v.fmFeedbackBefore=v.fmFeedbackLast;v.fmFeedbackLast=new float3(ra,rb,rd);
            return math.sin(pc*2*math.PI)*level.w;
        }
        void Paired(ref TrackerVoice v,in TrackerTone tone,in TrackerSample sample,float aL,float aR,ref float l,ref float r)
        {
            var clip=state.clips[tone.pcmB];var bSample=sample;bSample.loop=tone.loopB;bSample.loopStart=tone.loopStartB;bSample.loopEnd=tone.loopEndB;bSample.releaseExitsLoop=tone.releaseExitsLoopB;
            bSample.regionStart=0;bSample.regionEnd=clip.frames;
            double pos=v.positionB;
            if(tone.blendMode==3){pos+=aL*v.pmCurrent;pos%=clip.frames;if(pos<0)pos+=clip.frames;bSample.loop=SampleLoop.Off;}
            float bL=pos>=0&&pos<clip.frames?Pcm(in clip,in bSample,pos,0,v.released):0,bR=pos>=0&&pos<clip.frames?Pcm(in clip,in bSample,pos,clip.channels==2?1:0,v.released):0;
            if(tone.blendMode==1){bL*=aL;bR*=aR;}
            if(tone.blendMode==0){l=aL*(1-v.blendCurrent)+bL*v.blendCurrent;r=aR*(1-v.blendCurrent)+bR*v.blendCurrent;}else{l=bL*(1-v.blendCurrent)+aL*v.blendCurrent;r=bR*(1-v.blendCurrent)+aR*v.blendCurrent;}
        }
        void AdvanceB(ref TrackerVoice v,in TrackerTone tone,in TrackerSample sample,double previousA)
        {
            if(tone.pcmB<0)return;var clip=state.clips[tone.pcmB];var b=sample;b.loop=tone.loopB;b.loopStart=tone.loopStartB;b.loopEnd=tone.loopEndB;b.releaseExitsLoop=tone.releaseExitsLoopB;
            b.regionStart=0;b.regionEnd=clip.frames;
            double increment=SafeInitialStep(v.stepB,(v.commandPitch+v.commandArp+v.commandVibrato)/12d);
            // New global per-note pitch curves affect both members of a sample
            // pair. Keep historical scalar-only playback byte-compatible.
            if(HasParameterEnvelope(in v,TrackerParameter.FineTune))increment=SafeInitialStep(increment,(VoiceParameter(in v,TrackerParameter.FineTune)-tone.baseGlobalTune)/1200d);
            if(HasParameterEnvelope(in v,TrackerParameter.VibratoDepth)||HasParameterEnvelope(in v,TrackerParameter.VibratoRate)||HasParameterEnvelope(in v,TrackerParameter.VibratoFadeIn))increment*=v.vibratoSmooth;
            var cursor=new TrackerVoice{active=true,position=v.positionB+increment*v.directionB,direction=v.directionB,released=v.released};AdvancePcm(ref cursor,in b,in clip);v.positionB=cursor.position;v.directionB=cursor.direction;
            if(tone.blendMode==2&&v.direction>0&&v.position<previousA){v.positionB=tone.loopB==SampleLoop.Backward?tone.loopEndB-1:0;v.directionB=tone.loopB==SampleLoop.Backward?-1:1;}
        }
    }
}
