using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>
    /// Audio-side half of the song's command definitions (dynamic parameters). Each definition is one fixed-size record
    /// in deviceParameters; a row command or an automation lane writes its normalized value there, and this code moves
    /// the target there without a click: a 5 ms linear ramp, applied per sample to track gains/pans/width and in 4-sample
    /// steps to instrument parameters, macros and effect settings. A linear lane ramps exactly from one evaluation point to
    /// the next, so it follows the drawn line. Nothing here allocates, resolves a string or reads a managed object.
    /// </summary>
    public unsafe partial struct TrackerRealtime
    {
        // Track gains, pans and width are interpolated per sample over 5 ms. Instrument parameters, macros and effect
        // settings are read once per render slice, so they ramp over 10 ms in 4-sample steps (120 steps).
        public const double DynamicRampSeconds = .005, DynamicSteppedRampSeconds = .010;
        public const int DynamicSliceFrames = 4;

        int DynamicRampFrames(in TrackerDeviceParameter p) => (int)math.max(1, math.round(state.sampleRate * (p.kind == 4 ? DynamicRampSeconds : DynamicSteppedRampSeconds)));

        /// <summary>Normalized latent value → target units; the authored position maps back to the exact authored value.</summary>
        static float DynamicValue(in TrackerDeviceParameter p, float u)
            => u == p.authored ? p.destinationAuthored : (float)((double)p.min + ((double)p.max - p.min) * u);

        float ClampDynamic(in TrackerDeviceParameter p, float value)
        {
            switch (p.kind)
            {
                case 1: return math.saturate(value);
                case 2: return TrackerParameters.Clamp((TrackerParameter)p.parameter, value);
                case 3:
                    {
                        ref var chain = ref state.chains[p.chain];
                        int flat = chain.layout.paramOffset[p.node] + p.parameter;
                        return math.clamp(value, chain.layout.pMin[flat], chain.layout.pMax[flat]);
                    }
                case 4: return p.parameter == 1 || p.parameter == 3 ? math.clamp(value, -1, 1) : p.parameter == 4 ? math.clamp(value, 0, 4) : math.clamp(value, 0, 16);
                case 5: return math.clamp(value, 32, 999);
            }
            return value;
        }

        void EmitDynamic(int at)
        {
            var p = state.deviceParameters[at];
            float value = ClampDynamic(in p, DynamicValue(in p, p.latent));
            p.emitted = p.latent;
            if (p.kind == 5)
            {
                // Song tempo: a timing value, taken at the next row duration; never ramped.
                state.bpm = value;
                p.destination = p.target = value;
                p.laneRamp = false;
                state.deviceParameters[at] = p;
                return;
            }

            if (p.laneRamp && !state.silentReplay)
            {
                // A linear lane: start exactly on the line here and reach its value at the next evaluation point.
                p.laneRamp = false;
                p.destination = value;
                p.target = ClampDynamic(in p, DynamicValue(in p, math.saturate(p.laneNext)));
                p.remaining = (int)math.max(1, math.round(p.laneFrames));
                p.step = (float)(((double)p.target - p.destination) / p.remaining);
                p.stepping = true;
                state.deviceParameters[at] = p;
                ApplyDynamic(in p, value, value, 1);
                return;
            }

            p.laneRamp = false;
            if (value == p.target && (p.remaining > 0 || value == p.destination))
            {
                state.deviceParameters[at] = p;
                return;
            }

            p.target = value;
            if (state.silentReplay || !p.smooth)
            {
                // Seek replays silently: land on the value the row would have, with no ramp.
                p.destination = value;
                p.remaining = 0;
                p.step = 0;
                p.stepping = true;
                state.deviceParameters[at] = p;
                ApplyDynamic(in p, value, value, 1);
                return;
            }

            p.remaining = DynamicRampFrames(in p);
            p.step = (float)(((double)value - p.destination) / p.remaining);
            state.deviceParameters[at] = p;
        }

        /// <summary>The longest next render slice the ramps allow: a stepped ramp needs short slices; a per-sample one only needs to end on time.</summary>
        int DynamicSliceLimit(int frames)
        {
            for (int i = 0; i < state.dynamicCount; i++)
            {
                var p = state.deviceParameters[state.dynamicParameters[i]];
                if (p.remaining > 0)
                    frames = math.min(frames, p.kind == 4 ? p.remaining : DynamicSliceFrames);
            }
            return frames;
        }

        /// <summary>Called before each render slice of <paramref name="frames"/>; moves every ramping target along.</summary>
        void AdvanceDynamic(int frames)
        {
            for (int i = 0; i < state.dynamicCount; i++)
            {
                int at = state.dynamicParameters[i];
                var p = state.deviceParameters[at];
                if (p.remaining > 0)
                {
                    float start = p.destination;
                    float end = p.remaining <= frames ? p.target : (float)(start + (double)p.step * frames);
                    p.remaining = math.max(0, p.remaining - frames);
                    p.destination = end;
                    p.stepping = true;
                    state.deviceParameters[at] = p;
                    ApplyDynamic(in p, start, end, frames);
                }
                else if (p.stepping)
                {
                    // The ramp ended: hold the exact final value and stop per-sample stepping.
                    p.stepping = false;
                    state.deviceParameters[at] = p;
                    ApplyDynamic(in p, p.destination, p.destination, frames);
                }
            }
        }

        /// <summary>Writes the target for one slice that moves from <paramref name="start"/> to <paramref name="end"/>.</summary>
        void ApplyDynamic(in TrackerDeviceParameter p, float start, float end, int frames)
        {
            float held = start == end ? end : (start + end) * .5f;
            switch (p.kind)
            {
                case 1:
                    {
                        int at = p.instrument * 8 + p.parameter;
                        var m = state.macros[at];
                        m.value = m.target = math.saturate(held);
                        m.sliding = false;
                        state.macros[at] = m;
                        break;
                    }
                case 2:
                    {
                        int at = p.instrument * TrackerParameters.Stride + p.parameter;
                        state.parameterDirect[at] = TrackerParameters.Clamp((TrackerParameter)p.parameter, held);
                        state.parameterWritten[at] |= 1;
                        break;
                    }
                case 3:
                    {
                        ref var chain = ref state.chains[p.chain];
                        int flat = chain.layout.paramOffset[p.node] + p.parameter;
                        chain.layout.pBase[flat] = held;
                        // A modulated setting is ramped by the chain's own modulation from this base.
                        if (!p.ramped)
                        {
                            chain.processor.pLive[flat] = chain.processor.pStart[flat] = chain.processor.pTarget[flat] = held;
                            chain.processor.pStep[flat] = 0;
                        }
                        break;
                    }
                case 4:
                    {
                        var t = state.tracks[p.track];
                        float step = frames > 0 && start != end ? (end - start) / frames : 0;
                        switch (p.parameter)
                        {
                            case 0: t.preGain = start; t.preGainStep = step; break;
                            case 1: t.prePan = start; t.prePanStep = step; break;
                            case 4: t.width = start; t.widthStep = step; break;
                            case 2:
                                t.postGain = end; t.livePostGain = start;
                                t.gainRemaining = start == end ? 0 : frames; t.gainStep = step;
                                break;
                            case 3:
                                t.postPan = end; t.livePostPan = start;
                                t.panRemaining = start == end ? 0 : frames; t.panStep = step;
                                break;
                        }
                        state.tracks[p.track] = t;
                        break;
                    }
            }
        }

        /// <summary>Play, Stop and Seek: every command target returns exactly to its authored value, without a ramp.</summary>
        void ResetDynamic()
        {
            for (int i = 0; i < state.dynamicCount; i++)
            {
                int at = state.dynamicParameters[i];
                var p = state.deviceParameters[at];
                p.latent = p.emitted = p.authored;
                p.destination = p.target = p.destinationAuthored;
                p.remaining = 0;
                p.step = 0;
                p.stepping = false;
                p.laneRamp = false;
                state.deviceParameters[at] = p;
                if (p.kind == 3)
                {
                    ref var chain = ref state.chains[p.chain];
                    int flat = chain.layout.paramOffset[p.node] + p.parameter;
                    float authored = chain.authoredParameters[flat];
                    chain.layout.pBase[flat] = authored;
                    chain.processor.pLive[flat] = chain.processor.pStart[flat] = chain.processor.pTarget[flat] = authored;
                    chain.processor.pStep[flat] = 0;
                }
                else if (p.kind == 4)
                {
                    var t = state.tracks[p.track];
                    t.preGain = t.authoredPreGain; t.prePan = t.authoredPrePan; t.width = t.authoredWidth;
                    t.preGainStep = t.prePanStep = t.widthStep = 0;
                    state.tracks[p.track] = t;
                }
                // Instrument parameters, macros, post fader values and tempo are restored by the transport reset.
            }
        }

        /// <summary>
        /// For a linear lane aimed at a dynamic record: the value it should reach at the next evaluation point (the next
        /// tick or lane point, whichever comes first) and how many frames away that is.
        /// </summary>
        void DynamicLaneHint(in TrackerAutomation lane, double time)
        {
            var parameter = state.deviceParameters[lane.target];
            if (!parameter.dynamic || !lane.linear || state.silentReplay || state.held)
                return;
            int tpl = math.max(1, state.ticksPerLine);
            double tickTime = state.row + (math.floor((time - state.row) * tpl + 1e-9) + 1) / tpl;
            double next = math.min(tickTime, state.row + 1);
            for (int j = 0; j < lane.count; j++)
            {
                double t = state.automationPoints[lane.points + j].line;
                if (t > time && t < next)
                    next = t;
            }
            if (!(next > time))
                return;
            parameter.laneNext = LaneValue(in lane, next, parameter.authored);
            parameter.laneFrames = (next - time) * state.rowDuration;
            parameter.laneRamp = parameter.laneFrames >= 1;
            state.deviceParameters[lane.target] = parameter;
        }

        float LaneValue(in TrackerAutomation lane, double time, float authored)
        {
            float value = authored;
            var first = state.automationPoints[lane.points];
            if (time < first.line)
                return value;
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
                    return value;
                }
                value = b.value;
            }
            return value;
        }
    }
}
