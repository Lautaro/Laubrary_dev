using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public unsafe partial struct TrackerRealtime
    {
        void ResetAutomation()
        {
            for (int i = 0; i < state.deviceCount; i++)
            {
                var d = state.devices[i];
                d.enabled = !d.authoredEnabled;
                state.devices[i] = d;
                SetDeviceEnabled(i, d.authoredEnabled, false);
            }

            for (int i = 0; i < state.deviceParameterCount; i++)
            {
                var p = state.deviceParameters[i];
                p.latent = p.emitted = p.authored;
                p.written = p.device < 0 || p.authoredPresent;
                state.deviceParameters[i] = p;
                EmitDeviceParameter(i);
            }

            state.automationDeadline = double.PositiveInfinity;
        }

        void SetDeviceParameter(int at, float value)
        {
            if (at < 0 || at >= state.deviceParameterCount)
                return;
            var p = state.deviceParameters[at];
            p.latent = math.saturate(value);
            p.written = true;
            state.deviceParameters[at] = p;
            EmitDeviceParameter(at);
        }

        float SourceCurve(in TrackerDeviceParameter p, float value)
        {
            if (p.pointCount == 0)
                return value;
            var first = state.automationPoints[p.points];
            if (value <= first.line)
                return first.value;
            for (int i = 1; i < p.pointCount; i++)
            {
                var b = state.automationPoints[p.points + i];
                if (value <= b.line)
                {
                    var a = state.automationPoints[p.points + i - 1];
                    return math.lerp(a.value, b.value, (float)((value - a.line) / (b.line - a.line)));
                }
            }

            return state.automationPoints[p.points + p.pointCount - 1].value;
        }

        void EmitDeviceParameter(int at)
        {
            var p = state.deviceParameters[at];
            if (!p.written && p.device >= 0)
                return;
            if (p.device >= 0 && !state.devices[p.device].enabled && state.devices[p.device].kind != 2)
                return;
            float value = (float)((double)p.min + ((double)p.max - p.min) * SourceCurve(in p, p.latent));
            if (p.quantum > 0)
                value = p.lower + p.quantum * math.floor((value - p.lower) / p.quantum + .5f);
            p.emitted = p.latent;
            state.deviceParameters[at] = p;
            if (p.kind == 1)
            {
                var c = TrackerCommand.SetMacro(p.instrument, p.parameter, value);
                ApplyParameterCommand(in c);
            }
            else if (p.kind == 2)
            {
                var c = TrackerCommand.SetParameter(p.instrument, (TrackerParameter)p.parameter, value);
                ApplyParameterCommand(in c);
            }
            else if (p.kind == 6)
            {
                var c = TrackerCommand.SetExternal(p.route, value);
                ApplyParameterCommand(in c);
            }
            else if (p.kind == 3)
            {
                ref var chain = ref state.chains[p.chain];
                int target = chain.layout.paramOffset[p.node] + p.parameter;
                value = math.clamp(value, chain.layout.pMin[target], chain.layout.pMax[target]);
                chain.layout.pBase[target] = value;
                chain.processor.pLive[target] = chain.processor.pStart[target] = chain.processor.pTarget[target] = value;
                chain.processor.pStep[target] = 0;
            }
            else if (p.kind == 4)
            {
                var tr = state.tracks[p.track];
                switch (p.parameter)
                {
                    case 0:
                        tr.preGain = math.clamp(value, 0, 16);
                        break;
                    case 1:
                        tr.prePan = math.clamp(value * 2 - 1, -1, 1);
                        break;
                    case 2:
                        tr.postGain = tr.livePostGain = math.clamp(value, 0, 16);
                        tr.gainRemaining = 0;
                        break;
                    case 3:
                        tr.postPan = tr.livePostPan = math.clamp(value * 2 - 1, -1, 1);
                        tr.panRemaining = 0;
                        break;
                }

                state.tracks[p.track] = tr;
            }
            else if (p.kind == 7)
                SetDeviceEnabled(p.parameter, value != 0);
            else if (p.kind == 5)
            {
                switch (p.parameter)
                {
                    case 0:
                        state.bpm = value;
                        break;
                    case 1:
                        state.linesPerBeat = (int)math.floor(value + .5f);
                        break;
                    case 2:
                        state.ticksPerLine = (int)math.floor(value + .5f);
                        break;
                }
            }
        }

        void SetDeviceEnabled(int at, bool enabled, bool emit = true)
        {
            var device = state.devices[at];
            if (device.enabled == enabled)
                return;
            device.enabled = enabled;
            state.devices[at] = device;
            if (device.kind == 2)
            {
                for (int p = 0; p < device.count; p++)
                {
                    var parameter = state.deviceParameters[device.parameters + p];
                    if (parameter.chain < 0)
                        continue;
                    ref var chain = ref state.chains[parameter.chain];
                    int node = parameter.node;
                    chain.layout.enabled[node] = enabled;
                    chain.processor.presence[node] = chain.processor.presencePrev[node] = enabled ? 1 : 0;
                    if (!enabled)
                    {
                        int begin = chain.layout.stateOffset[node], end = node + 1 < chain.layout.nodeCount ? chain.layout.stateOffset[node + 1] : chain.layout.stateFloats;
                        for (int i = begin; i < end; i++)
                            chain.processor.arena[i] = 0;
                        ResetBypassedNode(in chain.layout, chain.processor.arena, node);
                    }
                }
            }

            if (enabled && emit)
                for (int p = 0; p < device.count; p++)
                    EmitDeviceParameter(device.parameters + p);
        }

        static void ResetBypassedNode(in Laubrary.Audio.SapChainLayout layout, Unity.Collections.NativeArray<float> arena, int node)
        {
            int start = layout.stateOffset[node], derived = layout.derivedOffset[node];
            switch (layout.nodeType[node])
            {
                case Laubrary.Zounds.ZoundEffectType.Delay:
                    Laubrary.Audio.DelayEffect.Reset(arena, start, layout.derivedFlat, derived);
                    break;
                case Laubrary.Zounds.ZoundEffectType.Reverb:
                    Laubrary.Audio.ReverbEffect.Reset(arena, start, layout.derivedFlat, derived);
                    break;
                case Laubrary.Zounds.ZoundEffectType.Flanger:
                case Laubrary.Zounds.ZoundEffectType.Chorus:
                    Laubrary.Audio.ModDelayEffect.Reset(arena, start, layout.derivedFlat[derived]);
                    break;
                case Laubrary.Zounds.ZoundEffectType.LowPass:
                case Laubrary.Zounds.ZoundEffectType.HighPass:
                    Laubrary.Audio.BiquadEffect.Reset(arena, start);
                    break;
                case Laubrary.Zounds.ZoundEffectType.EQ:
                    Laubrary.Audio.EqEffect.Reset(arena, start);
                    break;
            }
        }

        void EvaluateAutomation(double time, bool timingOnly = false)
        {
            int pattern = state.sequence[state.sequenceIndex].pattern;
            for (int p = 0; p < state.deviceParameterCount; p++)
                state.parameterDirty[p] = 0;
            for (int i = 0; i < state.automationCount; i++)
            {
                var lane = state.automation[i];
                if (lane.pattern != pattern || lane.count == 0 || timingOnly && !lane.timing || !timingOnly && lane.timing)
                    continue;
                float value = state.deviceParameters[lane.target].authored;
                var first = state.automationPoints[lane.points];
                if (time >= first.line)
                {
                    value = first.value;
                    for (int j = 1; j < lane.count; j++)
                    {
                        var b = state.automationPoints[lane.points + j];
                        if (time < b.line)
                        {
                            if (lane.linear)
                            {
                                var a = state.automationPoints[lane.points + j - 1];
                                value = math.lerp(a.value, b.value, (float)((time - a.line) / (b.line - a.line)));
                            }

                            break;
                        }

                        value = b.value;
                    }
                }

                var parameter = state.deviceParameters[lane.target];
                parameter.written = true;
                parameter.latent = math.saturate(value);
                state.deviceParameters[lane.target] = parameter;
                state.parameterDirty[lane.target] = 1;
            }

            for (int p = 0; p < state.deviceParameterCount; p++)
                if (state.parameterDirty[p] != 0 && state.deviceParameters[p].kind == 7)
                    EmitDeviceParameter(p);
            for (int p = 0; p < state.deviceParameterCount; p++)
                if (state.parameterDirty[p] != 0 && state.deviceParameters[p].kind != 7)
                    EmitDeviceParameter(p);
        }

        void ScheduleAutomationPoint(double after)
        {
            state.automationDeadline = double.PositiveInfinity;
            state.automationTime = double.PositiveInfinity;
            int pattern = state.sequence[state.sequenceIndex].pattern;
            if (state.held)
                return;
            for (int i = 0; i < state.automationCount; i++)
            {
                var lane = state.automation[i];
                if (lane.pattern != pattern || lane.timing)
                    continue;
                for (int j = 0; j < lane.count; j++)
                {
                    double time = state.automationPoints[lane.points + j].line;
                    if (time > after && time < state.row + 1 && time >= state.row && time < state.automationTime)
                    {
                        state.automationTime = time;
                        state.automationDeadline = state.rowStart + (time - state.row) * state.rowDuration;
                    }
                }
            }
        }

        void ApplyAutomationPoint()
        {
            if (!math.isfinite(state.automationDeadline))
                return;
            // Retain the source coordinate: reversing a cumulative binary64 frame
            // calculation can land below a Step point and reschedule it forever.
            double time = state.automationTime;
            EvaluateAutomation(time);
            ScheduleAutomationPoint(time);
        }
    }
}
