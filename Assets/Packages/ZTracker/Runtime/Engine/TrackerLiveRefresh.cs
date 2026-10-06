using System;
using System.Collections.Generic;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
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
            copy.name="";copy.diagnostics.Clear();copy.bpm=120;copy.linesPerBeat=4;copy.ticksPerLine=6;
            foreach(var t in copy.tracks){t.name="";t.color=Color.gray;t.columns.Clear();t.visibleEffectColumns=0;t.preVolume=t.postVolume=t.preWidth=1;t.prePan=t.postPan=0;t.triggerMute=t.outputMute=t.solo=false;foreach(var n in t.devices.nodes){n.p=Array.Empty<float>();n.enabled=true;}}
            foreach(var p in copy.patterns){p.name="";foreach(var t in p.tracks){t.lines.Clear();foreach(var lane in t.automation){lane.points.Clear();lane.interpolation=AutomationInterpolation.Step;}}}
            var key=new StringBuilder(JsonUtility.ToJson(copy));
            foreach(var i in source.instruments){if(i==null){key.Append("null");continue;}var data=ZTrackerMigration.Copy(i.model);var q=data.parameters;
                q.volume=q.pan=q.fineTune=q.blend=q.pulseWidth=q.waveBRatio=q.pmDepth=q.unisonDetune=q.unisonSpread=0;
                q.attack=q.decay=q.sustain=q.release=q.vibratoDepth=q.vibratoRate=q.vibratoFadeIn=q.instFilterCutoff=q.instFilterResonance=q.glideSeconds=q.fmFeedback=0;
                if(q.fmOperators!=null)for(int op=0;op<q.fmOperators.Length;op++){var value=q.fmOperators[op];value.freqRatio=value.freqFixed=value.level=value.attack=value.decay=value.sustain=value.release=0;q.fmOperators[op]=value;}
                data.sampler.volume=data.sampler.pan=data.sampler.fineTuneCents=0;foreach(var m in data.macros)m.value=0;key.Append(JsonUtility.ToJson(data));}
            for(int i=0;i<state.chainCount;i++){var l=state.chains[i].layout;key.Append('|').Append(l.stateFloats).Append(':').Append(l.paramCount);for(int j=0;j<l.nodeCount;j++)key.Append(':').Append((int)l.nodeType[j]).Append('/').Append(l.stateOffset[j]);for(int j=0;j<l.derivedFlat.Length;j++)key.Append('/').Append(l.derivedFlat[j]);}
            liveIdentity=key.ToString();
            // Each delayed note has at most three local commands (volume, pan, local FX).
            state.carryCells=state.cells.Length;state.carryOps=state.ops.Length;
            Expand(ref state.cells,state.carryCells+state.columnCount);
            Expand(ref state.ops,state.carryOps+state.columnCount*3);
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
            for(int i=0;i<old.macros.Length;i++){var m=next.macros[i];if(m.authored==old.macros[i].authored)next.macros[i]=old.macros[i];}
            CopyLive(old.parameterDirect,next.parameterDirect);CopyLive(old.parameterLive,next.parameterLive);CopyLive(old.parameterWritten,next.parameterWritten);
            for(int i=0;i<old.parameterBase.Length;i++)if(next.parameterBase[i]!=old.parameterBase[i]){next.parameterDirect[i]=next.parameterLive[i]=next.parameterBase[i];next.parameterWritten[i]|=1;}
            CopyLive(old.descriptors,next.descriptors);CopyLive(old.commandMemory,next.commandMemory);CopyLive(old.occurrences,next.occurrences);
            for(int i=0;i<old.deviceCount;i++){var d=next.devices[i];if(d.authoredEnabled==old.devices[i].authoredEnabled)d.enabled=old.devices[i].enabled;next.devices[i]=d;}
            for(int i=0;i<old.deviceParameterCount;i++){var p=next.deviceParameters[i];var before=old.deviceParameters[i];if(p.authored==before.authored&&p.authoredPresent==before.authoredPresent){p.latent=before.latent;p.emitted=before.emitted;p.written=before.written;}next.deviceParameters[i]=p;}
            for(int i=0;i<old.columnCount;i++){var col=next.columns[i];if(col.pendingCell>=0){var cell=old.cells[col.pendingCell];int dest=next.carryCells+i;for(int j=0;j<cell.opCount;j++)next.ops[next.carryOps+i*3+j]=old.ops[cell.ops+j];cell.ops=next.carryOps+i*3;next.cells[dest]=cell;col.pendingCell=dest;next.columns[i]=col;}}
            for(int i=0;i<old.trackCount;i++){var before=old.tracks[i];var after=next.tracks[i];after.instrumentGain=before.instrumentGain;
                if(after.authoredPreGain==before.authoredPreGain)after.preGain=before.preGain;if(after.authoredPrePan==before.authoredPrePan)after.prePan=before.prePan;if(after.authoredWidth==before.authoredWidth)after.width=before.width;
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
            next.playing=old.playing;next.paused=old.paused;next.held=old.held;next.holdRemaining=old.holdRemaining;next.rowPending=old.rowPending;next.loopSong=old.loopSong;next.normalization=old.normalization;next.rowOccurrence=old.rowOccurrence;next.automationTime=old.automationTime;next.automationDeadline=old.automationDeadline;
            SapRenderTicket.Enter(old.ticket);SapRenderTicket.Exit(old.ticket,0,true);state=next;EvaluateParameters();ScheduleAutomationPoint(state.automationTime);Emit(TrackerEventKind.PreparedSwap);
        }
    }
}
