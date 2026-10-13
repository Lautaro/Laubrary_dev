using System;
using System.Collections.Generic;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    public sealed unsafe partial class TrackerPreparedSong
    {
        // Managed metadata captured before publication. Guards never inspect mutable audio arrays.
        string liveIdentity;
        void BuildLiveIdentity(SongData source)
        {
            var copy=ZTrackerMigration.Copy(source);
            // Detached candidates own fresh Unity wrappers. Musical identity is
            // the ordered model IDs/payloads below, not wrapper instance IDs.
            copy.instruments.Clear();
            copy.name="";copy.diagnostics.Clear();copy.bpm=120;copy.linesPerBeat=4;copy.ticksPerLine=6;
            foreach(var t in copy.tracks){t.name="";t.color=Color.gray;t.columns.Clear();t.effectColumnViews?.Clear();t.envelopeColumns?.Clear();t.visibleEffectColumns=0;t.preVolume=t.postVolume=t.preWidth=1;t.prePan=t.postPan=0;t.triggerMute=t.outputMute=t.solo=false;NormalizeLiveChain(t.devices);}
            foreach(var t in copy.tracks)
            {
                if(t.sourceDevices!=null)foreach(var s in t.sourceDevices){if(s==null)continue;s.pluginId="";if(s.parameters!=null)foreach(var p in s.parameters){if(p==null)continue;p.defaultValue=0;p.parameter="";}}
                if(t.externalSources!=null)foreach(var s in t.externalSources)if(s!=null)s.pluginId="";
            }
            // A command's id, name and colour can change while playing; its target and range shape the compiled record.
            if(copy.dynamicParameters!=null)foreach(var d in copy.dynamicParameters)if(d!=null){d.id="";d.name="";d.color=Color.clear;}
            // Envelope column lanes come and go with their first and last point; a built-in one changes the record count, which the structural check catches.
            foreach(var p in copy.patterns){p.name="";foreach(var t in p.tracks){t.lines.Clear();t.automation?.RemoveAll(ZTrackerEnvelopes.IsEnvelopeLane);if(t.automation!=null)foreach(var lane in t.automation){if(lane==null)continue;lane.points?.Clear();lane.interpolation=AutomationInterpolation.Step;}}}
            var key=new StringBuilder(JsonUtility.ToJson(copy));
            foreach(var i in source.instruments){if(i==null){key.Append("null");continue;}key.Append('|').Append(i.playbackSourceIdentity!=0?i.playbackSourceIdentity:i.GetInstanceID());var data=ZTrackerMigration.Copy(i.model);var q=data.parameters;
                q.volume=q.pan=q.fineTune=q.blend=q.pulseWidth=q.waveBRatio=q.pmDepth=q.unisonDetune=q.unisonSpread=0;
                q.attack=q.decay=q.sustain=q.release=q.vibratoDepth=q.vibratoRate=q.vibratoFadeIn=q.instFilterCutoff=q.instFilterResonance=q.glideSeconds=q.fmFeedback=0;
                // These definitions are read from the new tone/sample on each rendered block.
                // They do not alter native memory topology or invalidate held oscillator phases.
                q.waveA=q.waveB=q.blendMode=q.fmAlgorithm=q.instFilterMode=0;
                q.enumDomain=SoundEnumDomain.NativeDirect;
                q.instFilterEnabled=q.glideEnabled=q.glideLegato=q.legatoEnabled=q.legatoSlides=q.blendEnvelope=false; q.transpose=0;
                q.legatoPoly=false;q.legatoVelocity=LegatoVelocity.Follow;q.legatoVelocitySeconds=q.legatoVelocityScale=0;
                q.blendAttack=q.blendDecay=q.blendSustain=q.blendRelease=q.vibratoRandomness=q.instDelaySend=q.instReverbSend=0;
                // The renderer always has four native FM slots, including when the
                // authored subtractive instrument has not initialized its operators.
                q.fmOperators=new ZTrackerInstrument.FMOperatorData[4];
                if(data.family==InstrumentFamily.Synth){data.synthMode=SynthMode.Subtractive;q.type=InstrumentType.Synth;q.unisonVoices=1;data.sampler.nna=NewNoteAction.Cut;}
                NormalizeParameterEnvelope(q.blendEnvelopeData);NormalizeParameterEnvelope(q.pulseWidthEnvelopeData);NormalizeParameterEnvelope(q.waveBRatioEnvelopeData);NormalizeParameterEnvelope(q.pmDepthEnvelopeData);NormalizeParameterEnvelope(q.unisonDetuneEnvelopeData);
                if(q.parameterEnvelopes!=null)foreach(var envelope in q.parameterEnvelopes)if(envelope!=null)NormalizeParameterEnvelope(envelope.envelope);
                data.sampler.volume=data.sampler.pan=data.sampler.fineTuneCents=0;foreach(var s in data.sampler.samples){s.volume=s.pan=0;s.name="";}foreach(var m in data.macros)m.value=0;
                foreach(var chain in data.fxChains)NormalizeLiveChain(chain);
                key.Append(JsonUtility.ToJson(data));}
            for(int i=0;i<state.chainCount;i++){var l=state.chains[i].layout;key.Append('|').Append(l.stateFloats).Append(':').Append(l.paramCount);for(int j=0;j<l.nodeCount;j++)key.Append(':').Append((int)l.nodeType[j]).Append('/').Append(l.stateOffset[j]);for(int j=0;j<l.derivedFlat.Length;j++)key.Append('/').Append(l.derivedFlat[j]);}
            liveIdentity=key.ToString();
            foreach(var link in drumLinks)liveIdentity+="|drum:"+link.kit+":"+link.source+":"+link.mapping.noteMin+":"+link.mapping.noteMax+":"+link.mapping.baseNote+":"+link.mapping.keyTracking+":"+link.mapping.gateSeconds;
            // Each delayed note has at most three local commands (volume, pan, local FX).
            state.carryCells=state.cells.Length;state.carryOps=state.ops.Length;
            Expand(ref state.cells,state.carryCells+state.columnCount);
            Expand(ref state.ops,state.carryOps+state.columnCount*3);
        }
        static void NormalizeLiveChain(AudioEffectChainData chain)
        {
            if(chain==null)return;
            if(chain.nodes!=null)foreach(var n in chain.nodes){if(n==null)continue;n.p=Array.Empty<float>();n.enabled=true;}
            if(chain.modifiers!=null)foreach(var m in chain.modifiers){if(m==null)continue;m.name="";if(m.type!=Laubrary.Zounds.ZoundModifierType.Code)m.p=Array.Empty<float>();if(m.steps!=null)for(int i=0;i<m.steps.Length;i++)m.steps[i]=0;if(m.curve?.m_points!=null)for(int i=0;i<m.curve.m_points.Count;i++){var p=m.curve.m_points[i];p.time=p.value=p.exponent=p.randomX=p.randomY=p.randomBias=0;m.curve.m_points[i]=p;}}
            if(chain.bindings!=null)foreach(var b in chain.bindings){if(b==null)continue;b.depth=0;b.schema=2;b.combine=Laubrary.Zounds.Dsp.ModulationCombine.Set;b.op=Laubrary.Zounds.ModifierOp.Replace;}
        }
        static void NormalizeParameterEnvelope(ZUIEnvelopeData envelope)
        {
            if(envelope==null)return;
            envelope.loopStart=envelope.loopEnd=0;
            envelope.sustainPosition=0;envelope.durationInRows=false;
            // Loop and sustain switches, direction and length are read from the new envelope on every rendered sample.
            envelope.loopEnabled=envelope.sustainEnabled=false;envelope.loopMode=0;envelope.xMin=envelope.xMax=envelope.yMin=envelope.yMax=0;
            // The merged LFO (T-0641) is read per sample from the compiled envelope, so switching it or moving its sliders applies live.
            envelope.lfo=false;envelope.lfoDepth=envelope.lfoSpeed=envelope.lfoDelay=0;envelope.lfoShape=0;
            // ADSR stages (T-0639) only write the points and the compiled ADSR flag; the envelope's Maximum is display only.
            envelope.adsr=envelope.adsrSet=envelope.customMaximum=false;envelope.adsrAttack=envelope.adsrDecay=envelope.adsrSustain=envelope.adsrRelease=envelope.adsrStart=0;
            if(envelope.points!=null)foreach(var point in envelope.points){point.time=point.value=0;point.exponent=1;}
        }
        static void Expand<T>(ref NativeArray<T> values,int size) where T:unmanaged
        {var expanded=new NativeArray<T>(Math.Max(1,size),Allocator.Persistent);for(int i=0;i<values.Length;i++)expanded[i]=values[i];values.Dispose();values=expanded;}
        public bool PreparePreserving(TrackerPreparedSong previous,out string reason)
        {
            reason=null;
            if(previous==null||Disposed||Published||previous.Disposed||liveIdentity!=previous.liveIdentity||state.sampleRate!=previous.state.sampleRate||state.maxFrames!=previous.state.maxFrames||state.deviceCount!=previous.state.deviceCount||state.deviceParameterCount!=previous.state.deviceParameterCount||state.chainCount!=previous.state.chainCount||state.modStride!=previous.state.modStride||state.columnCount!=previous.state.columnCount||state.voices.Length!=previous.state.voices.Length){reason="Live update pending: stop and play to apply structural changes";return false;}
            // Keep every historical memory bank, even if the edited rows no longer mention it.
            var existing=new Dictionary<string,int>(previous.memoryIds);
            var remap=new int[memoryIds.Count];
            foreach(var pair in memoryIds){if(!existing.TryGetValue(pair.Key,out int index)){index=existing.Count;existing.Add(pair.Key,index);}remap[pair.Value]=index;}
            for(int i=0;i<state.opCount;i++){var op=state.ops[i];if(op.bank>=0){op.bank=remap[op.bank];state.ops[i]=op;}}
            memoryIds.Clear();foreach(var pair in existing)memoryIds.Add(pair.Key,pair.Value);
            state.commandMemory.Dispose();state.commandMemory=Buffer<int>(existing.Count);
            return true;
        }
    }
    public unsafe partial struct TrackerRealtime
    {
        static void CopyLive<T>(NativeArray<T> old,NativeArray<T> next) where T:unmanaged
        {for(int i=0;i<old.Length&&i<next.Length;i++)next[i]=old[i];}
        void PreserveSwap(TrackerState next)
        {
            var old=state;
            CopyLive(old.voices,next.voices);CopyLive(old.modulationState,next.modulationState);CopyLive(old.columns,next.columns);
            AdaptSynthMembers(in old,ref next);
            // Held cursors retain a step computed at launch from the old authored tuning.
            // Move that base with the authored definition; live direct/macro deltas remain
            // relative to the new base and therefore preserve unrelated game overrides.
            for(int i=0;i<next.voices.Length;i++)
            {
                var v=next.voices[i];if(!v.active||v.sample<0||v.sample>=old.tones.Length||v.sample>=next.tones.Length)continue;
                var before=old.tones[v.sample];var after=next.tones[v.sample];
                double cents=(double)after.baseGlobalTune-before.baseGlobalTune+after.transposeCents-before.transposeCents;
                // Unison detune is also part of the launch-time step. Keep its authored
                // base in sync while the retained live detune continues smoothing.
                if(after.kind!=0){int at=v.instrument*TrackerParameters.Stride+(int)TrackerParameter.UnisonDetune;cents+=v.memberSpread*((double)next.parameterBase[at]-old.parameterBase[at]);}
                if(cents!=0){v.step=SafeInitialStep(v.step,cents/1200d);next.voices[i]=v;}
            }
            for(int i=0;i<old.macros.Length;i++){var m=next.macros[i];if(m.authored==old.macros[i].authored)next.macros[i]=old.macros[i];}
            CopyLive(old.parameterDirect,next.parameterDirect);CopyLive(old.parameterLive,next.parameterLive);CopyLive(old.parameterWritten,next.parameterWritten);
            for(int i=0;i<old.parameterBase.Length;i++)if(next.parameterBase[i]!=old.parameterBase[i]){next.parameterDirect[i]=next.parameterLive[i]=next.parameterBase[i];next.parameterWritten[i]|=1;}
            CopyLive(old.descriptors,next.descriptors);CopyLive(old.commandMemory,next.commandMemory);CopyLive(old.occurrences,next.occurrences);
            for(int i=0;i<old.deviceCount;i++){var d=next.devices[i];if(d.authoredEnabled==old.devices[i].authoredEnabled)d.enabled=old.devices[i].enabled;next.devices[i]=d;}
            for(int i=0;i<old.deviceParameterCount;i++){var p=next.deviceParameters[i];var before=old.deviceParameters[i];if(p.authored==before.authored&&p.authoredPresent==before.authoredPresent){p.latent=before.latent;p.emitted=before.emitted;p.written=before.written;}
                // A command definition keeps where its target is and where it is heading, if it still points at the same place.
                if(p.dynamic&&before.dynamic&&p.kind==before.kind&&p.parameter==before.parameter&&p.track==before.track&&p.instrument==before.instrument&&p.chain==before.chain&&p.node==before.node&&p.destinationAuthored==before.destinationAuthored){p.destination=before.destination;p.target=before.target;p.step=before.step;p.remaining=before.remaining;p.stepping=before.stepping;p.laneOwned=before.laneOwned;p.laneEnvelope=before.laneEnvelope;p.typedRow=before.typedRow;}
                if(p.dynamic&&before.dynamic&&p.kind==before.kind){p.runtimePending=before.runtimePending;p.runtimeValue=before.runtimeValue;}
                next.deviceParameters[i]=p;}
            for(int i=0;i<old.columnCount;i++){var col=next.columns[i];if(col.pendingCell>=0){var cell=old.cells[col.pendingCell];int dest=next.carryCells+i;for(int j=0;j<cell.opCount;j++)next.ops[next.carryOps+i*3+j]=old.ops[cell.ops+j];cell.ops=next.carryOps+i*3;next.cells[dest]=cell;col.pendingCell=dest;next.columns[i]=col;}}
            for(int i=0;i<old.trackCount;i++){var before=old.tracks[i];var after=next.tracks[i];after.instrumentGain=before.instrumentGain;after.runtimeTranspose=before.runtimeTranspose;after.runtimeDetune=before.runtimeDetune;
                if(after.authoredPreGain==before.authoredPreGain){after.preGain=before.preGain;after.preGainStep=before.preGainStep;}if(after.authoredPrePan==before.authoredPrePan){after.prePan=before.prePan;after.prePanStep=before.prePanStep;}if(after.authoredWidth==before.authoredWidth){after.width=before.width;after.widthStep=before.widthStep;}
                if(after.authoredPostGain==before.authoredPostGain){after.postGain=before.postGain;after.livePostGain=before.livePostGain;after.gainRemaining=before.gainRemaining;after.gainStep=before.gainStep;}else {after.livePostGain=before.livePostGain;after.gainRemaining=64;after.gainStep=(after.postGain-before.livePostGain)/64;}
                if(after.authoredPostPan==before.authoredPostPan){after.postPan=before.postPan;after.livePostPan=before.livePostPan;after.panRemaining=before.panRemaining;after.panStep=before.panStep;}else {after.livePostPan=before.livePostPan;after.panRemaining=64;after.panStep=(after.postPan-before.livePostPan)/64;}
                if(after.authoredOutputMute==before.authoredOutputMute)after.outputMute=before.outputMute;if(after.authoredTriggerMute==before.authoredTriggerMute)after.triggerMute=before.triggerMute;after.output=before.output;next.tracks[i]=after;}
            for(int i=0;i<old.chainCount;i++){var a=old.chains[i].processor;ref var b=ref next.chains[i].processor;
                CopyLive(a.arena,b.arena);CopyLive(a.pLive,b.pLive);CopyLive(a.pStart,b.pStart);CopyLive(a.pStep,b.pStep);CopyLive(a.pTarget,b.pTarget);CopyLive(a.modValue,b.modValue);CopyLive(a.modCtlLive,b.modCtlLive);CopyLive(a.modCtlTarget,b.modCtlTarget);CopyLive(a.presence,b.presence);CopyLive(a.presencePrev,b.presencePrev);
                b.rng=a.rng;b.curveSeed=a.curveSeed;b.elapsedSamples=a.elapsedSamples;
                for(int p=0;p<next.chains[i].layout.paramCount;p++)if(old.chains[i].authoredParameters[p]==next.chains[i].authoredParameters[p])next.chains[i].layout.pBase[p]=old.chains[i].layout.pBase[p];else {float value=next.chains[i].layout.pBase[p];b.pLive[p]=b.pStart[p]=b.pTarget[p]=value;b.pStep[p]=0;}}
            next.samplePosition=old.samplePosition;next.transportOrigin=old.transportOrigin;next.cohort=old.cohort;next.sequenceIndex=old.sequenceIndex;next.row=old.row;next.tick=old.tick;next.breakRow=old.breakRow;
            next.rowStart=old.rowStart;next.rowEnd=old.rowEnd;next.rowDuration=old.rowDuration;next.clockCompensation=old.clockCompensation;next.tickDeadline=old.tickDeadline;next.tickRemaining=old.tickRemaining;
            next.liveTimingPending=old.liveTimingPending||next.authoredBpm!=old.authoredBpm||next.authoredLinesPerBeat!=old.authoredLinesPerBeat||next.authoredTicksPerLine!=old.authoredTicksPerLine;next.bpm=old.bpm;next.linesPerBeat=old.linesPerBeat;next.ticksPerLine=old.ticksPerLine;
            next.runtimeTempo=old.runtimeTempo;next.runtimeTempoApplied=old.runtimeTempoApplied;next.underlyingBpm=old.underlyingBpm;
            // A new preparation owns fresh history. Do not copy megabytes on the audio thread or alias native owners.
            // Recording resumes at the next pattern start; an active tape operation is explicitly cancelled.
            next.rewind.completed=old.rewind.completed;next.rewind.refused=old.rewind.refused;
            next.rewind.result=old.rewind.phase!=TrackerRewindPhase.Idle?TrackerRewindResult.Cancelled:old.rewind.result;
            next.rowReverse=old.rowReverse;
            if(next.rowReverse.active){next.rowReverse.active=false;next.rowReverse.result=TrackerRowReverseResult.Cancelled;next.rowReverse.cancelled++;next.liveTimingPending=true;}
            next.playing=old.playing;next.paused=old.paused;next.held=old.held;next.holdRemaining=old.holdRemaining;next.rowPending=old.rowPending;next.loopSong=old.loopSong;next.normalization=old.normalization;next.limiterReduction=old.limiterReduction;next.rowOccurrence=old.rowOccurrence;next.rowSerial=old.rowSerial;next.dynamicLanding=old.dynamicLanding;next.automationTime=old.automationTime;next.automationDeadline=old.automationDeadline;
            SapRenderTicket.Enter(old.ticket);SapRenderTicket.Exit(old.ticket,0,true);state=next;EvaluateParameters();
            // Sustain and release slopes are cached by held voices rather than read anew
            // every frame. Refresh only an edited stage, preserving its age and phase.
            for (int i=0;i<state.voices.Length;i++)
            {
                var voice=state.voices[i]; if (!voice.active) continue;
                int at=voice.instrument*TrackerParameters.Stride;
                if (voice.stage==3 && old.parameterBase[at+(int)TrackerParameter.Sustain]!=state.parameterBase[at+(int)TrackerParameter.Sustain]) voice.envelope=VoiceParameter(in voice,TrackerParameter.Sustain);
                if (voice.stage==4 && old.parameterBase[at+(int)TrackerParameter.Release]!=state.parameterBase[at+(int)TrackerParameter.Release])
                {
                    float seconds=VoiceParameter(in voice,TrackerParameter.Release);
                    voice.releaseStep=seconds>0?voice.envelope/(seconds*state.sampleRate):1;
                }
                var before=old.tones[voice.sample];var after=state.tones[voice.sample];
                if (old.samples[voice.sample].filterType!=state.samples[voice.sample].filterType) {voice.filterCutoff=-1;voice.filterQ=-1;}
                if (voice.blendStage==3 && before.blendSustain!=after.blendSustain) voice.blendLevel=after.blendSustain;
                for (int op=0;op<4;op++)
                {
                    var parameter=(TrackerParameter)((int)TrackerParameter.Op0Ratio+op*7+5);
                    if (voice.fmStage[op]==3 && old.parameterBase[at+(int)parameter]!=state.parameterBase[at+(int)parameter]) voice.fmLevel[op]=VoiceParameter(in voice,parameter);
                }
                state.voices[i]=voice;
            }
            ScheduleAutomationPoint(state.automationTime);Emit(TrackerEventKind.PreparedSwap);
        }
        static void AdaptSynthMembers(in TrackerState old,ref TrackerState next)
        {
            for(int first=0;first<next.voices.Length;first++)
            {
                var template=next.voices[first];if(!template.active)continue;
                var before=old.tones[template.sample];var after=next.tones[template.sample];
                if(before.kind==0||after.kind==0||(before.members==after.members&&before.kind==after.kind))continue;
                bool seen=false;for(int n=0;n<first;n++){var prior=next.voices[n];if(prior.active&&prior.cohort==template.cohort&&prior.sample==template.sample){seen=true;break;}}if(seen)continue;
                int count=0;for(int n=first;n<next.voices.Length;n++){var voice=next.voices[n];if(voice.active&&voice.cohort==template.cohort&&voice.sample==template.sample)count++;}
                int available=count;for(int n=0;n<next.voices.Length;n++)if(!next.voices[n].active)available++;
                int wanted=math.min(after.members,available),member=0;
                // Additional members inherit the held oscillator/envelope state;
                // bounded native slots are the only storage used on the audio thread.
                for(int n=0;n<next.voices.Length&&count<wanted;n++)if(!next.voices[n].active)
                {
                    var added=template;added.modulationOffset=n*next.modStride;next.voices[n]=added;
                    for(int m=0;m<next.modStride;m++)next.modulationState[added.modulationOffset+m]=next.modulationState[template.modulationOffset+m];count++;
                }
                for(int n=0;n<next.voices.Length;n++)
                {
                    var voice=next.voices[n];if(!voice.active||voice.cohort!=template.cohort||voice.sample!=template.sample)continue;
                    if(member>=wanted){voice.active=false;next.voices[n]=voice;continue;}
                    float spread=wanted>1?member*(2f/(wanted-1))-1:0;
                    float oldDetune=old.parameterBase[voice.instrument*TrackerParameters.Stride+(int)TrackerParameter.UnisonDetune];
                    voice.step=SafeInitialStep(voice.step,(spread-voice.memberSpread)*oldDetune/1200d);
                    voice.memberSpread=spread;voice.memberGain=1/math.sqrt((float)wanted);member++;
                    if(before.kind!=after.kind)
                    {
                        if(after.kind==2)
                        {
                            voice.fmPhase=new float4(voice.phaseA);
                            for(int op=0;op<4;op++){int at=voice.instrument*TrackerParameters.Stride+(int)TrackerParameter.Op0Ratio+op*7;voice.fmLevel[op]=next.parameterBase[at+5];voice.fmReleaseStart[op]=voice.fmLevel[op];voice.fmStage[op]=voice.released?4:3;}
                        }
                        else {voice.phaseA=voice.fmPhase.x;voice.phaseB=voice.fmPhase.y;}
                    }
                    next.voices[n]=voice;
                }
            }
        }
    }
}
