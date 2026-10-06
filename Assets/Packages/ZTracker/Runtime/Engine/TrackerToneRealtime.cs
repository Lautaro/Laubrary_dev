using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public unsafe partial struct TrackerRealtime
    {
        float Parameter(int instrument,TrackerParameter p)=>state.parameterLive[instrument*TrackerParameters.Stride+(int)p];
        bool Written(int instrument,TrackerParameter p)=>state.parameterWritten[instrument*TrackerParameters.Stride+(int)p]!=0;
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
            return route.min+(route.max-route.min)*value;
        }
        float PointCurve(int start,int count,float time,bool exponent)
        {
            if(count==0)return 0;var first=state.points[start];if(time<=(float)first.time)return first.value;
            for(int n=1;n<count;n++){var b=state.points[start+n];if(time<=(float)b.time){var a=state.points[start+n-1];float t=(time-(float)a.time)/((float)b.time-(float)a.time);if(exponent)t=math.pow(t,b.exponent);return a.value+(b.value-a.value)*t;}}
            return state.points[start+count-1].value;
        }
        float ToneCurve(in TrackerMod m,float time)
        {
            if(m.loopEnabled&&time>(float)m.loopEnd){float width=(float)(m.loopEnd-m.loopStart);float phase=(time-(float)m.loopStart)%width;if(m.loop==SampleLoop.PingPong&&((int)((time-(float)m.loopStart)/width)&1)!=0)phase=width-phase;time=(float)m.loopStart+phase;}
            return PointCurve(m.points,m.pointCount,time,true);
        }
        void InitTone(ref TrackerVoice v,in TrackerTone tone,in TrackerSample sample,int member,int previousNote,bool held)
        {
            v.memberSpread=tone.members>1?(member/(float)(tone.members-1))*2-1:0;v.memberGain=1/math.sqrt((float)tone.members);
            v.blendCurrent=Parameter(v.instrument,TrackerParameter.Blend);v.pmCurrent=Parameter(v.instrument,TrackerParameter.PMDepth);v.ratioCurrent=Parameter(v.instrument,TrackerParameter.WaveBRatio);v.pulseCurrent=Parameter(v.instrument,TrackerParameter.PulseWidth);v.detuneCurrent=Parameter(v.instrument,TrackerParameter.UnisonDetune);
            v.noiseA=Seed((uint)v.cohort,(uint)member,1);v.noiseB=Seed((uint)v.cohort,(uint)member,2);v.directionB=tone.loopB==SampleLoop.Backward?-1:1;v.positionB=tone.loopB==SampleLoop.Backward?tone.loopEndB-1:0;
            v.vibratoSmooth=v.arpSmooth=1;v.vibratoFade=Parameter(v.instrument,TrackerParameter.VibratoFadeIn)>0?0:1;
            if(tone.kind!=0){float freq=440*math.pow(2f,(v.note-69)/12f);float fine=math.pow(2f,(Parameter(v.instrument,TrackerParameter.FineTune)+v.memberSpread*v.detuneCurrent)/1200);v.step=(freq*fine)/(double)state.sampleRate;}
            if(tone.pcmB>=0){var clip=state.clips[tone.pcmB];float ne=(v.note-69)*(1f/12f),be=(tone.baseNoteB-69)*(1f/12f),fe=tone.fineTuneB*(1f/1200f);v.stepB=sample.legacyPan?(math.pow(2f,(ne-be)+fe)*math.asfloat(0x3f7fffff))*(clip.frequency/(double)state.sampleRate):math.pow(2d,(v.note-tone.baseNoteB+tone.fineTuneB/100)/12d)*(clip.frequency/(double)state.sampleRate);}
            if(tone.glide&&(!tone.legato||held)&&previousNote!=v.note)v.glideCurrent=math.pow(2d,(previousNote-v.note)/12d);else v.glideCurrent=1;
            float speed=PointCurve(tone.arpPoints,tone.arpPointCount,0,false);v.arpStep=math.max(1,speed*state.sampleRate/(tone.arpPerNote?1:math.max(1,tone.arpNoteCount)));
            if(tone.arpNoteCount>=2){float first=state.points[tone.arpNotes].value;v.step*=math.pow(2d,first/12d);}
            // Main ADSR remains per voice. Macros alter its rates without touching the saved asset.
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
            if(tone.glide){double seconds=Parameter(ii,TrackerParameter.GlideSeconds);v.glideCurrent+=(1-v.glideCurrent)*(seconds>0?1-math.exp(-1/(seconds*state.sampleRate)):1);step*=v.glideCurrent;}
            double cadence=state.playing?state.sampleRate*60d/(state.bpm*state.linesPerBeat*state.ticksPerLine):3528;
            if(tone.arpNoteCount>=2){v.arpTime+=1f/state.sampleRate;v.arpCounter++;if(v.arpCounter>=v.arpStep){v.arpCounter-=v.arpStep;v.arpIndex=(v.arpIndex+1)%tone.arpNoteCount;float speed=PointCurve(tone.arpPoints,tone.arpPointCount,v.arpTime,false);v.arpStep=math.max(1,speed*state.sampleRate/(tone.arpPerNote?1:tone.arpNoteCount));}
                float note=state.points[tone.arpNotes+v.arpIndex].value-state.points[tone.arpNotes].value;double target=math.pow(2f,note/12f);v.arpSmooth+=(target-v.arpSmooth)/math.max(1,cadence);step*=v.arpSmooth;
            }
            // Retained sampler vibrato uses P3's modulation device unless explicitly routed live.
            bool vibrato=tone.kind!=0||Written(ii,TrackerParameter.VibratoDepth)||Written(ii,TrackerParameter.VibratoRate);
            if(vibrato){float depth=Parameter(ii,TrackerParameter.VibratoDepth),rate=Parameter(ii,TrackerParameter.VibratoRate),fade=Parameter(ii,TrackerParameter.VibratoFadeIn);v.vibratoFade=fade>0?math.min(1,v.vibratoFade+1/(fade*state.sampleRate)):1;v.vibratoPhase+=rate/state.sampleRate*2*math.PI;if(v.vibratoPhase>2*math.PI)v.vibratoPhase-=2*math.PI;double target=math.pow(2f,math.sin(v.vibratoPhase)*depth*v.vibratoFade/1200);v.vibratoSmooth+=(target-v.vibratoSmooth)/math.max(1,cadence);step*=v.vibratoSmooth;}
            float fine=Parameter(ii,TrackerParameter.FineTune)-tone.baseGlobalTune;
            if(tone.kind!=0)fine+=v.memberSpread*(v.detuneCurrent-state.parameterBase[ii*TrackerParameters.Stride+(int)TrackerParameter.UnisonDetune]);
            return step*math.pow(2d,pitch/12d+fine/1200d);
        }
        void ToneTargets(ref TrackerVoice v,in TrackerTone tone)
        {
            int ii=v.instrument;float blend=Parameter(ii,TrackerParameter.Blend),pw=Parameter(ii,TrackerParameter.PulseWidth),ratio=Parameter(ii,TrackerParameter.WaveBRatio),pm=Parameter(ii,TrackerParameter.PMDepth),detune=Parameter(ii,TrackerParameter.UnisonDetune);
            if(tone.blendEnvelope){if(v.released){if(v.blendStage!=4){v.blendStage=4;v.blendReleaseStart=v.blendLevel;}v.blendLevel=math.max(0,v.blendLevel-(tone.blendRelease>0?1/(tone.blendRelease*state.sampleRate):1));}else if(v.blendStage==0){v.blendLevel+=tone.blendAttack>0?1/(tone.blendAttack*state.sampleRate):1;if(v.blendLevel>=1){v.blendLevel=1;v.blendStage=2;}}else if(v.blendStage==2){v.blendLevel-=tone.blendDecay>0?(1-tone.blendSustain)/(tone.blendDecay*state.sampleRate):1;if(v.blendLevel<=tone.blendSustain){v.blendLevel=tone.blendSustain;v.blendStage=3;}}v.blendCurrent=v.blendLevel;}
            for(int n=0;n<5;n++){var env=state.toneEnvelopes[tone.envelopes+n];if(env.pointCount==0)continue;float value=ToneCurve(in env,v.envTime);switch(n){case 0:blend=value;break;case 1:pw=value;break;case 2:ratio=value;break;case 3:pm=value;break;case 4:detune=value;break;}}
            float smooth=1/(.005f*state.sampleRate);v.blendCurrent+=(blend-v.blendCurrent)*smooth;v.pulseCurrent+=(pw-v.pulseCurrent)*smooth;v.ratioCurrent+=(ratio-v.ratioCurrent)*smooth;v.pmCurrent+=(pm-v.pmCurrent)*smooth;v.detuneCurrent+=(detune-v.detuneCurrent)*smooth;v.envTime+=1f/state.sampleRate;
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
                int p=v.instrument*TrackerParameters.Stride+(int)TrackerParameter.Op0Ratio+op*7;float attack=state.parameterLive[p+3],decay=state.parameterLive[p+4],sustain=state.parameterLive[p+5],release=state.parameterLive[p+6];
                if(v.released){if(v.fmStage[op]!=4){v.fmReleaseStart[op]=v.fmLevel[op];v.fmStage[op]=4;}v.fmLevel[op]=math.max(0,v.fmLevel[op]-(release>0?v.fmReleaseStart[op]/(release*state.sampleRate):1));}
                else if(v.fmStage[op]==0){v.fmLevel[op]+=attack>0?1/(attack*state.sampleRate):1;if(v.fmLevel[op]>=1){v.fmLevel[op]=1;v.fmStage[op]=2;}}
                else if(v.fmStage[op]==2){v.fmLevel[op]-=decay>0?(1-sustain)/(decay*state.sampleRate):1;if(v.fmLevel[op]<=sustain){v.fmLevel[op]=sustain;v.fmStage[op]=3;}}
                level[op]=v.fmLevel[op]*state.parameterLive[p+2];inc[op]=state.parameterLive[p+1]>0?state.parameterLive[p+1]/state.sampleRate:increment*state.parameterLive[p];
            }
            float feedback=Parameter(v.instrument,TrackerParameter.FMFeedback)*v.fmPrevious;
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
        void Paired(ref TrackerVoice v,in TrackerTone tone,in TrackerSample sample,float aL,float aR,ref float l,ref float r)
        {
            var clip=state.clips[tone.pcmB];var bSample=sample;bSample.loop=tone.loopB;bSample.loopStart=tone.loopStartB;bSample.loopEnd=tone.loopEndB;bSample.releaseExitsLoop=tone.releaseExitsLoopB;
            double pos=v.positionB;
            if(tone.blendMode==3){pos+=aL*v.pmCurrent;pos%=clip.frames;if(pos<0)pos+=clip.frames;bSample.loop=SampleLoop.Off;}
            float bL=pos>=0&&pos<clip.frames?Pcm(in clip,in bSample,pos,0,v.released):0,bR=pos>=0&&pos<clip.frames?Pcm(in clip,in bSample,pos,clip.channels==2?1:0,v.released):0;
            if(tone.blendMode==1){bL*=aL;bR*=aR;}
            if(tone.blendMode==0){l=aL*(1-v.blendCurrent)+bL*v.blendCurrent;r=aR*(1-v.blendCurrent)+bR*v.blendCurrent;}else{l=bL*(1-v.blendCurrent)+aL*v.blendCurrent;r=bR*(1-v.blendCurrent)+aR*v.blendCurrent;}
        }
        void AdvanceB(ref TrackerVoice v,in TrackerTone tone,in TrackerSample sample,double previousA)
        {
            if(tone.pcmB<0)return;var clip=state.clips[tone.pcmB];var b=sample;b.loop=tone.loopB;b.loopStart=tone.loopStartB;b.loopEnd=tone.loopEndB;b.releaseExitsLoop=tone.releaseExitsLoopB;
            var cursor=new TrackerVoice{active=true,position=v.positionB+v.stepB*v.directionB,direction=v.directionB,released=v.released};AdvancePcm(ref cursor,in b,in clip);v.positionB=cursor.position;v.directionB=cursor.direction;
            if(tone.blendMode==2&&v.direction>0&&v.position<previousA)v.positionB=0;
        }
    }
}
