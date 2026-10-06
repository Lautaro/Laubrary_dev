using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Unity.Mathematics;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    public sealed unsafe partial class TrackerPreparedSong
    {
        public readonly Dictionary<string,int> externalRouteIds=new Dictionary<string,int>();
        public readonly List<CompiledParameterSet> parameterSets=new List<CompiledParameterSet>();
        public static string ExternalAddress(string track,string device,int slot)=>track+"/"+device+"/"+slot;
        public bool TryExternalCommand(string track,string device,int slot,float value,out TrackerCommand command)
        {
            if(externalRouteIds.TryGetValue(ExternalAddress(track,device,slot),out int route)&&math.isfinite(value)){command=TrackerCommand.SetExternal(route,value);return true;}command=default;return false;
        }
        void BuildP4(SongData song,int rate,List<TrackerSample> samples,List<TrackerZone> zones,List<TrackerInstrument> insts,List<float> pcm,List<TrackerPcm> clips,Dictionary<AudioClip,int> pcmMap,List<TrackerModPoint> points)
        {
            var blends=new Dictionary<int,SampleBlendExtension>();
            for(int i=0;i<insts.Count;i++)if(song.instruments[i]!=null&&song.instruments[i].model.family==InstrumentFamily.Sampler){int zi=insts[i].zoneStart;foreach(var z in song.instruments[i].model.sampler.zones)if(!z.inactive){if(z.blend!=null&&z.blend.pcmB!=null){var zone=zones[zi];samples.Add(samples[zone.sample]);zone.sample=samples.Count-1;zones[zi]=zone;blends[zone.sample]=z.blend;}zi++;}}
            var tones=new List<TrackerTone>();var envs=new List<TrackerMod>();var bases=new List<float>();var macros=new List<TrackerMacroValue>();var routes=new List<TrackerParameterRoute>();var external=new List<TrackerParameterRoute>();
            var ids=new Dictionary<string,int>();for(int i=0;i<song.instruments.Count;i++)if(song.instruments[i]!=null){string id=song.instruments[i].model.id;if(ids.ContainsKey(id))diagnostics.Add("DUPLICATE_INSTRUMENT_IDENTITY "+id);else ids.Add(id,i);}
            for(int i=0;i<insts.Count;i++){
                var data=song.instruments[i]?.model;var q=data?.parameters??new InstrumentParameters();
                var values=new float[TrackerParameters.Stride];
                values[(int)TrackerParameter.Volume]=data?.family==InstrumentFamily.Sampler?data.sampler.volume:q.volume;
                values[(int)TrackerParameter.Pan]=data?.family==InstrumentFamily.Sampler?data.sampler.pan:q.pan;
                values[(int)TrackerParameter.FineTune]=data?.family==InstrumentFamily.Sampler?data.sampler.fineTuneCents:q.fineTune;
                values[(int)TrackerParameter.Blend]=q.blend;values[(int)TrackerParameter.PulseWidth]=q.pulseWidth;values[(int)TrackerParameter.WaveBRatio]=q.waveBRatio;values[(int)TrackerParameter.PMDepth]=q.pmDepth;
                values[(int)TrackerParameter.UnisonDetune]=q.unisonDetune;values[(int)TrackerParameter.UnisonSpread]=q.unisonSpread;
                values[(int)TrackerParameter.Attack]=q.attack;values[(int)TrackerParameter.Decay]=q.decay;values[(int)TrackerParameter.Sustain]=q.sustain;values[(int)TrackerParameter.Release]=q.release;
                values[(int)TrackerParameter.VibratoDepth]=q.vibratoDepth;values[(int)TrackerParameter.VibratoRate]=q.vibratoRate;values[(int)TrackerParameter.VibratoFadeIn]=q.vibratoFadeIn;
                values[(int)TrackerParameter.FilterCutoff]=q.instFilterCutoff*rate*.5f;values[(int)TrackerParameter.FilterResonance]=q.instFilterResonance;values[(int)TrackerParameter.GlideSeconds]=q.glideSeconds;values[(int)TrackerParameter.FMFeedback]=q.fmFeedback;
                for(int op=0;op<4;op++){
                    var o=q.fmOperators!=null&&op<q.fmOperators.Length?q.fmOperators[op]:new ZTrackerInstrument.FMOperatorData{freqRatio=1,level=1,attack=.001f,decay=.1f,sustain=.8f,release=.3f};
                    int at=(int)TrackerParameter.Op0Ratio+op*7;values[at]=o.freqRatio;values[at+1]=o.freqFixed;values[at+2]=o.level;values[at+3]=o.attack;values[at+4]=o.decay;values[at+5]=o.sustain;values[at+6]=o.release;
                    if(o.waveform!=0)diagnostics.Add("FM_SINE_OPERATORS_ONLY slot="+i+" op="+op);
                }
                foreach(float x in values)if(!math.isfinite(x))throw new ArgumentException("Nonfinite tone parameter slot="+i);
                for(int p=0;p<values.Length;p++)values[p]=TrackerParameters.Clamp((TrackerParameter)p,values[p]);bases.AddRange(values);
                for(int m=0;m<8;m++){float value=data?.macros[m].value??0;macros.Add(new TrackerMacroValue{value=value,target=value,authored=value});}
                if(data==null)continue;
                parameterSets.AddRange(ZTrackerMigration.ResolveParameterSets(data));
                for(int m=0;m<8;m++)foreach(var map in data.macros[m].mappings)if(CompileRoute(map,i,m,ids,points,out var route))routes.Add(route);
            }
            for(int si=0;si<samples.Count;si++){
                var sm=samples[si];var data=song.instruments[sm.instrument].model;var q=data.parameters;bool synth=data.family==InstrumentFamily.Synth;blends.TryGetValue(si,out var b);
                var tone=new TrackerTone{kind=synth?(data.synthMode==SynthMode.FM?2:1):0,waveA=CompileWave(q.waveA,q.enumDomain),waveB=CompileWave(q.waveB,q.enumDomain),blendMode=b?.mode??q.blendMode,members=synth&&data.synthMode!=SynthMode.FM?math.clamp(q.unisonVoices,1,8):1,pcmB=-1,envelopes=envs.Count,algorithm=q.fmAlgorithm>=0&&q.fmAlgorithm<=5?q.fmAlgorithm:5,glide=q.glideEnabled,legato=q.glideLegato,arpPerNote=q.arpeggioSpeedIsPerNote,baseGlobalVolume=synth?q.volume:data.sampler.volume,baseGlobalPan=synth?q.pan:data.sampler.pan,baseGlobalTune=synth?q.fineTune:data.sampler.fineTuneCents,blendEnvelope=b?.envelopeEnabled??q.blendEnvelope,blendAttack=b?.attack??q.blendAttack,blendDecay=b?.decay??q.blendDecay,blendSustain=b?.sustain??q.blendSustain,blendRelease=b?.release??q.blendRelease};
                if(tone.blendMode<0||tone.blendMode>3)throw new ArgumentException("Unknown blend mode");
                if(synth&&q.fmAlgorithm>5)diagnostics.Add("FM_ALGORITHM_FALLBACK authored="+q.fmAlgorithm+" compiled=5 slot="+sm.instrument);
                if(synth&&q.enumDomain==SoundEnumDomain.SavedAuthoring)diagnostics.Add("SAVED_WAVE_MAPPING slot="+sm.instrument+" original="+q.waveA+" compiled="+tone.waveA);
                var envelopes=new[]{b?.blendEnvelope??q.blendEnvelopeData,q.pulseWidthEnvelopeData,q.waveBRatioEnvelopeData,b?.pmEnvelope??q.pmDepthEnvelopeData,q.unisonDetuneEnvelopeData};
                foreach(var env in envelopes)envs.Add(CompileEnvelope(env,q.enumDomain,points));
                if(b!=null){tone.pcmB=ReadP4Clip(b.pcmB,pcm,clips,pcmMap);tone.baseNoteB=b.baseNoteB;tone.fineTuneB=b.fineTuneBCents;tone.loopB=b.loopB;tone.loopStartB=b.loopStartFrameB;tone.loopEndB=b.loopEndFrameB;tone.releaseExitsLoopB=b.releaseExitsLoopB;}
                tone.arpNotes=points.Count;tone.arpNoteCount=q.arpeggioEnabled?q.arpeggioNotes?.Length??0:0;
                if(tone.arpNoteCount>64)throw new ArgumentException("Arpeggio note capacity");
                for(int n=0;n<tone.arpNoteCount;n++)points.Add(new TrackerModPoint{time=n,value=q.arpeggioNotes[n],exponent=1});
                tone.arpPoints=points.Count;
                if(q.arpeggioSpeedPoints!=null&&q.arpeggioSpeedPoints.Count>0){foreach(var p in q.arpeggioSpeedPoints)points.Add(new TrackerModPoint{time=p.time,value=p.value,exponent=1});tone.arpPointCount=q.arpeggioSpeedPoints.Count;}else {points.Add(new TrackerModPoint{time=0,value=q.arpeggioSpeed,exponent=1});tone.arpPointCount=1;}
                if(synth){sm.delaySend=q.instDelaySend;sm.reverbSend=q.instReverbSend;sm.delayDestination=song.tracks.FindIndex(t=>t.kind==TrackKind.Send&&t.devices.nodes.Any(n=>n.type==Laubrary.Zounds.ZoundEffectType.Delay));sm.reverbDestination=song.tracks.FindIndex(t=>t.kind==TrackKind.Send&&t.devices.nodes.Any(n=>n.type==Laubrary.Zounds.ZoundEffectType.Reverb));if(sm.delaySend!=0&&sm.delayDestination<0||sm.reverbSend!=0&&sm.reverbDestination<0)throw new ArgumentException("Synth sends require explicit matching Send tracks");samples[si]=sm;}
                tones.Add(tone);
            }
            foreach(var track in song.tracks)if(track.externalSources!=null)foreach(var device in track.externalSources){
                if(string.IsNullOrEmpty(device.id)||string.IsNullOrEmpty(device.pluginId)||!ids.TryGetValue(device.instrumentId,out int ii)){diagnostics.Add("EXTERNAL_SOURCE_UNRESOLVED track="+track.id+" device="+device.id);continue;}
                var data=song.instruments[ii].model;
                for(int slot=0;slot<device.parameterNumbers.Count;slot++){
                    string id=device.parameterNumbers[slot];var map=data.externalParameters.Find(x=>x.externalId==id);
                    if(map==null){diagnostics.Add("EXTERNAL_ID_UNMAPPED device="+device.id+" slot="+slot+" external="+id);continue;}
                    if(CompileRoute(map.mapping,ii,-1,ids,points,out var route)){string address=ExternalAddress(track.id,device.id,slot);if(externalRouteIds.ContainsKey(address))throw new ArgumentException("Duplicate external source address");externalRouteIds.Add(address,external.Count);external.Add(route);}
                }
            }
            if(routes.Count>65536||external.Count>65536)throw new ArgumentException("Parameter route capacity exceeded");
            state.tones=Native(tones);state.toneEnvelopes=Native(envs);state.parameterBase=Native(bases);state.parameterDirect=Native(bases);state.parameterLive=Native(bases);state.parameterWritten=Buffer<byte>(bases.Count);state.macros=Native(macros);state.macroRoutes=Native(routes);state.externalRoutes=Native(external);
            // Native arrays use a sentinel element for empty data; retain real route counts.
            macroRouteCount=routes.Count;externalRouteCount=external.Count;state.macroRouteCount=routes.Count;state.externalRouteCount=external.Count;
        }
        public int macroRouteCount,externalRouteCount;
        int ReadP4Clip(AudioClip clip,List<float> pcm,List<TrackerPcm> clips,Dictionary<AudioClip,int> map)
        {
            if(map.TryGetValue(clip,out int index))return index;
            if(clip.channels<1||clip.channels>2||clip.samples<1||clip.loadType!=AudioClipLoadType.DecompressOnLoad||(long)pcm.Count+(long)clip.samples*clip.channels>67108864)throw new ArgumentException("Invalid B PCM/capacity");
            var raw=new float[checked(clip.samples*clip.channels)];if(!clip.GetData(raw,0))throw new ArgumentException("B PCM read refused");float peak=0;foreach(float v in raw){if(!math.isfinite(v))throw new ArgumentException("Nonfinite B PCM");peak=math.max(peak,math.abs(v));}
            index=clips.Count;map.Add(clip,index);clips.Add(new TrackerPcm{offset=pcm.Count,frames=clip.samples,channels=clip.channels,frequency=clip.frequency,peak=peak});pcm.AddRange(raw);return index;
        }
        static int CompileWave(int wave,SoundEnumDomain domain)
        {
            if(domain==SoundEnumDomain.NativeDirect){if(wave<0||wave>6)throw new ArgumentException("Unknown native wave");return wave;}
            switch(wave){case 0:return 0;case 1:return 4;case 2:return 2;case 3:return 1;case 4:return 5;default:throw new ArgumentException("Unknown saved wave");}
        }
        TrackerMod CompileEnvelope(ZUIEnvelopeData env,SoundEnumDomain domain,List<TrackerModPoint> points)
        {
            var m=new TrackerMod{points=points.Count};if(env==null||!env.enabled||env.Count==0)return m;
            if(env.Count>4096)throw new ArgumentException("Envelope capacity");float prev=-1;
            foreach(var p in env.points){if(!math.isfinite(p.time)||!math.isfinite(p.value)||!math.isfinite(p.exponent)||p.time<=prev||p.exponent<=0)throw new ArgumentException("Malformed tone envelope");points.Add(new TrackerModPoint{time=p.time,value=p.value,exponent=p.exponent});prev=p.time;}
            m.pointCount=env.Count;m.loopStart=env.loopStart;m.loopEnd=env.loopEnd;m.loopEnabled=env.loopEnabled;
            if(m.loopEnabled){int mode=domain==SoundEnumDomain.NativeDirect?env.loopMode:env.loopMode-1;if(mode<0||mode>1||m.loopEnd<=m.loopStart||m.loopStart<0||m.loopEnd>prev)throw new ArgumentException("Malformed tone loop");m.loop=mode==1?SampleLoop.PingPong:SampleLoop.Forward;}
            return m;
        }
        bool CompileRoute(Mapping map,int owner,int macro,Dictionary<string,int> ids,List<TrackerModPoint> points,out TrackerParameterRoute route)
        {
            route=default;var t=map.target;int instrument=owner;
            if(t.unresolved||(t.instrumentId!=""&&!ids.TryGetValue(t.instrumentId,out instrument))){diagnostics.Add("PARAMETER_ROUTE_UNRESOLVED "+t.parameter);return false;}
            int parameter;
            if(t.kind==ParameterKind.InstrumentMacro&&macro<0&&t.index>=0&&t.index<8)parameter=-1-t.index;
            else if((t.kind==ParameterKind.Synth||t.kind==ParameterKind.Sample)&&TrackerParameters.Resolve(t.parameter,out var p)){
                parameter=(int)p;string units=TrackerParameters.Units(p);
                if(t.units!="legacy parameter units"&&t.units!=units&&!(p==TrackerParameter.PMDepth&&(t.units=="cycles"||t.units=="frames"))){diagnostics.Add("PARAMETER_UNITS_UNSUPPORTED "+t.parameter+" units="+t.units);return false;}
            }else {diagnostics.Add("PARAMETER_TARGET_UNSUPPORTED "+t.parameter);return false;}
            if(map.curvePoints==null||map.curvePoints.Count==0){if(map.curve!=1){diagnostics.Add("PARAMETER_TRANSFORM_UNSUPPORTED "+t.parameter);return false;}}
            if(map.curvePoints?.Count>4096)throw new ArgumentException("Macro curve capacity");
            route=new TrackerParameterRoute{instrument=instrument,macro=owner*8+macro,parameter=parameter,min=map.min,max=map.max,points=points.Count,pointCount=map.curvePoints?.Count??0};
            if(map.curvePoints!=null)foreach(var p in map.curvePoints)points.Add(new TrackerModPoint{time=p.time,value=p.value,exponent=1});
            // Legacy filter endpoints are normalized Nyquist, declared Hz routes are physical.
            if(parameter==(int)TrackerParameter.FilterCutoff&&t.units=="legacy parameter units"){route.min*=state.sampleRate*.5f;route.max*=state.sampleRate*.5f;}
            return true;
        }
    }
}
