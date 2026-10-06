using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public sealed unsafe partial class TrackerPreparedSong
    {
        static float QuantizedSourceEndpoint(SourceParameterData source, float endpoint)
        {
            return source.quantum > 0 ? source.lower + source.quantum * math.floor((endpoint - source.lower) / source.quantum + .5f) : endpoint;
        }

        static bool SourceRangeFits(SourceParameterData source, float minimum, float maximum)
        {
            float a = QuantizedSourceEndpoint(source, source.min), b = QuantizedSourceEndpoint(source, source.max);
            return math.min(source.min, source.max) >= minimum && math.max(source.min, source.max) <= maximum && math.isfinite(a) && math.isfinite(b) && math.min(a, b) >= minimum && math.max(a, b) <= maximum;
        }

        void BuildAutomation(SongData song, List<TrackerChain> chains)
        {
            var devices = new List<TrackerDevice>();
            var parameters = new List<TrackerDeviceParameter>();
            var knots = new List<TrackerAutomationPoint>();
            var lanes = new List<TrackerAutomation>();
            var ids = new Dictionary<string, int>();
            for (int i = 0; i < song.instruments.Count; i++)
                if (song.instruments[i] != null && song.instruments[i].model.id != "" && !ids.ContainsKey(song.instruments[i].model.id))
                    ids.Add(song.instruments[i].model.id, i);
            for (int ti = 0; ti < song.tracks.Count; ti++)
            {
                var track = song.tracks[ti];
                var sources = new List<SourceDeviceData>(track.sourceDevices ?? new List<SourceDeviceData>());
                if (sources.Any(s => s == null))
                    diagnostics.Add("SOURCE_METADATA_MALFORMED track=" + track.id + " device=null");
                for (int i = 0; i < track.externalSources.Count; i++)
                {
                    var e = track.externalSources[i];
                    if (sources.Any(s => s != null && s.id == e.id))
                        continue;
                    var d = new SourceDeviceData
                    {
                        id = e.id,
                        ordinal = e.sourceOrdinal,
                        kind = SourceDeviceKind.InstrumentAutomation,
                        instrumentId = e.instrumentId,
                        pluginId = e.pluginId
                    };
                    for (int p = 0; p < e.parameterNumbers.Count; p++)
                        d.parameters.Add(new SourceParameterData { ordinal = p + 1, externalId = e.parameterNumbers[p] });
                    sources.Add(d);
                }

                var ordinals = new HashSet<int>();
                foreach (var src in sources.Where(s => s != null).OrderBy(s => s.ordinal))
                {
                    if (src.kind == SourceDeviceKind.Unsupported || !Enum.IsDefined(typeof(SourceDeviceKind), src.kind))
                    {
                        diagnostics.Add("SOURCE_DEVICE_UNSUPPORTED track=" + track.id + " device=" + src.id + " raw=" + src.rawSource);
                        continue;
                    }

                    if (src.ordinal < 1 || src.ordinal > 34 || !ordinals.Add(src.ordinal))
                    {
                        diagnostics.Add("DEVICE_ORDINAL_UNSUPPORTED track=" + track.id + " device=" + src.id);
                        continue;
                    }

                    if (src.parameters == null || src.parameters.Any(p => p == null))
                        diagnostics.Add("SOURCE_METADATA_MALFORMED track=" + track.id + " device=" + src.id);
                    int di = devices.Count;
                    var device = new TrackerDevice
                    {
                        track = ti,
                        ordinal = src.ordinal,
                        parameters = parameters.Count,
                        kind = (int)src.kind,
                        enabled = src.enabled,
                        authoredEnabled = src.enabled
                    };
                    var parameterOrdinals = new HashSet<int>();
                    foreach (var sp in (src.parameters ?? new List<SourceParameterData>()).Where(p => p != null).OrderBy(p => p.ordinal))
                    {
                        var p = new TrackerDeviceParameter
                        {
                            device = di,
                            ordinal = sp.ordinal,
                            track = ti,
                            instrument = -1,
                            chain = -1,
                            node = -1,
                            route = -1,
                            min = sp.min,
                            max = sp.max,
                            quantum = sp.quantum,
                            lower = sp.lower,
                            latent = sp.defaultValue,
                            emitted = sp.defaultValue,
                            authored = sp.defaultValue,
                            authoredPresent = (track.sourceDevices ?? new List<SourceDeviceData>()).Any(s => ReferenceEquals(s, src)),
                            points = knots.Count,
                            pointCount = sp.curvePoints?.Count ?? 0
                        };
                        if (sp.ordinal < 1 || sp.ordinal > 35 || !parameterOrdinals.Add(sp.ordinal) || !math.isfinite(sp.defaultValue) || sp.defaultValue < 0 || sp.defaultValue > 1 || !math.isfinite(sp.min) || !math.isfinite(sp.max) || !math.isfinite(sp.quantum) || !math.isfinite(sp.lower) || sp.quantum < 0 || sp.scaling != "Linear")
                        {
                            diagnostics.Add("SOURCE_PARAMETER_UNSUPPORTED device=" + src.id + " slot=" + sp.ordinal);
                            continue;
                        }

                        bool valid = false;
                        if (src.kind != SourceDeviceKind.AudioChain && (sp.units != "normalized" || !SourceRangeFits(sp, 0, 1)))
                        {
                            diagnostics.Add((sp.units != "normalized" ? "SOURCE_UNITS_MISMATCH" : "SOURCE_DOMAIN_UNSUPPORTED") + " device=" + src.id + " slot=" + sp.ordinal);
                            continue;
                        }

                        if (src.kind == SourceDeviceKind.InstrumentAutomation && externalRouteIds.TryGetValue(ExternalAddress(track.id, src.id, sp.ordinal - 1), out int external))
                        {
                            p.kind = 6;
                            p.route = external;
                            valid = true;
                        }

                        if (src.kind == SourceDeviceKind.InstrumentMacros && ids.TryGetValue(src.instrumentId, out int ii) && sp.ordinal <= 8 && sp.units == "normalized" && math.min(sp.min, sp.max) >= 0 && math.max(sp.min, sp.max) <= 1)
                        {
                            p.kind = 1;
                            p.instrument = ii;
                            p.parameter = sp.ordinal - 1;
                            valid = true;
                        }

                        if (src.kind == SourceDeviceKind.AudioChain && sp.explicitEquivalence && sp.target != null)
                        {
                            int node = track.devices.nodes.FindIndex(n => n.uid == sp.target.deviceId);
                            var tr = state.tracks[ti];
                            for (int ci = 0; ci < tr.chainCount; ci++)
                            {
                                int chainIndex = tr.chainStart + ci, begin = ci == 0 ? 0 : chains[chainIndex - 1].position, end = chains[chainIndex].position;
                                if (node >= begin && node < end && sp.target.index >= 0 && sp.target.index < chains[chainIndex].layout.paramCountOf[node - begin])
                                {
                                    int local = node - begin, param = sp.target.index, flat = chains[chainIndex].layout.paramOffset[local] + param;
                                    var type = chains[chainIndex].layout.nodeType[local];
                                    bool memorySizing = type == Laubrary.Zounds.ZoundEffectType.Delay && param == 3;
                                    bool units = sp.units == TrackerFxCompiler.ParameterUnits(type, param);
                                    bool discrete = (type == Laubrary.Zounds.ZoundEffectType.Delay && param == 4) || (type == Laubrary.Zounds.ZoundEffectType.Chorus && param == 2) || (type == Laubrary.Zounds.ZoundEffectType.Phaser && param == 2) || (type == Laubrary.Zounds.ZoundEffectType.BitCrush && param == 0);
                                    // This source model has no explicit allowed-value table for DSP enums.
                                    // A quantum alone cannot prove equivalence to a discrete source domain.
                                    if (memorySizing || !units || discrete || !SourceRangeFits(sp, chains[chainIndex].layout.pMin[flat], chains[chainIndex].layout.pMax[flat]))
                                    {
                                        diagnostics.Add((memorySizing ? "MEMORY_SIZING_TARGET" : !units ? "SOURCE_UNITS_MISMATCH" : discrete ? "DISCRETE_DOMAIN_UNDECLARED" : "SOURCE_DOMAIN_UNSUPPORTED") + " device=" + src.id + " slot=" + sp.ordinal);
                                        continue;
                                    }

                                    p.kind = 3;
                                    p.chain = chainIndex;
                                    p.node = local;
                                    p.parameter = param;
                                    valid = true;
                                }
                            }
                        }

                        if (!valid)
                        {
                            diagnostics.Add("SOURCE_TARGET_UNRESOLVED device=" + src.id + " slot=" + sp.ordinal);
                            continue;
                        }

                        if (sp.curvePoints != null && sp.curvePoints.Any(k => k == null))
                        {
                            diagnostics.Add("SOURCE_CURVE_UNSUPPORTED device=" + src.id);
                            continue;
                        }

                        if (sp.curvePoints != null && sp.curvePoints.Count > 0)
                        {
                            double prev = -1;
                            bool ok = sp.curvePoints[0].time == 0 && sp.curvePoints[0].value == 0 && sp.curvePoints[sp.curvePoints.Count - 1].time == 1 && sp.curvePoints[sp.curvePoints.Count - 1].value == 1;
                            foreach (var k in sp.curvePoints)
                            {
                                ok &= math.isfinite(k.time) && k.time > prev && k.time <= 1 && math.isfinite(k.value) && k.value >= 0 && k.value <= 1;
                                prev = k.time;
                            }

                            if (!ok)
                            {
                                diagnostics.Add("SOURCE_CURVE_UNSUPPORTED device=" + src.id);
                                continue;
                            }

                            foreach (var k in sp.curvePoints)
                                knots.Add(new TrackerAutomationPoint { line = k.time, value = k.value });
                        }

                        parameters.Add(p);
                    }

                    device.count = parameters.Count - device.parameters;
                    if (device.count > 0 && !parameters[device.parameters].authoredPresent)
                        diagnostics.Add("SOURCE_DEFAULT_UNDECLARED track=" + track.id + " device=" + src.id + "; destination defaults retained until a source write");
                    devices.Add(device);
                }
            }

            for (int pi = 0; pi < song.patterns.Count; pi++)
                foreach (var pt in song.patterns[pi].tracks)
                {
                    int owner = song.tracks.FindIndex(t => t.id == pt.trackId);
                    foreach (var lane in pt.automation)
                    {
                        if (lane == null || lane.target == null || lane.points == null || lane.points.Any(p => p == null))
                        {
                            diagnostics.Add("AUTOMATION_LANE_MALFORMED pattern=" + pi);
                            continue;
                        }

                        if (!lane.enabled)
                            continue;
                        var t = lane.target;
                        int ti = t.trackId == "" ? owner : song.tracks.FindIndex(tr => tr.id == t.trackId);
                        int target = -1;
                        bool timing = t.kind == ParameterKind.Timing;
                        bool valid = !lane.unsupported && !t.unresolved && ti >= 0 && lane.scaling == 0 && (lane.sourceMode == "" || lane.sourceMode == "Points" || lane.sourceMode == "Lines") && Enum.IsDefined(typeof(AutomationInterpolation), lane.interpolation) && math.isfinite(lane.timeQuantum) && lane.timeQuantum >= 1d / 256;
                        var sorted = lane.points.OrderBy(p => p.line).ToList();
                        double prev = -1;
                        foreach (var point in sorted)
                        {
                            valid &= math.isfinite(point.line) && math.isfinite(point.value) && point.line >= 0 && point.line <= song.patterns[pi].lineCount && point.line > prev && point.value >= 0 && point.value <= 1 && point.line * 256 == math.floor(point.line * 256) && (!timing || point.line == math.floor(point.line)) && math.abs(point.line / lane.timeQuantum - math.round(point.line / lane.timeQuantum)) < 1e-8;
                            prev = point.line;
                        }

                        if (!valid)
                        {
                            diagnostics.Add("AUTOMATION_LANE_UNSUPPORTED pattern=" + pi + " lane=" + lane.id + " raw=" + lane.rawSource);
                            continue;
                        }

                        if (t.kind == ParameterKind.Device)
                        {
                            int sd = (song.tracks[ti].sourceDevices ?? new List<SourceDeviceData>()).FindIndex(d => d != null && d.id == t.deviceId);
                            var legacySource = song.tracks[ti].externalSources.Find(d => d.id == t.deviceId);
                            int ordinal = sd >= 0 ? song.tracks[ti].sourceDevices[sd].ordinal : legacySource?.sourceOrdinal ?? 0;
                            int di = devices.FindIndex(d => d.track == ti && d.ordinal == ordinal);
                            if (di >= 0)
                            {
                                var d = devices[di];
                                if (t.parameter == "enabled")
                                {
                                    if (lane.interpolation != AutomationInterpolation.Step || sorted.Any(point => point.value != 0 && point.value != 1))
                                    {
                                        diagnostics.Add("BYPASS_AUTOMATION_UNSUPPORTED lane=" + lane.id);
                                        continue;
                                    }

                                    target = parameters.Count;
                                    parameters.Add(new TrackerDeviceParameter { device = -1, kind = 7, parameter = di, min = 0, max = 1, authored = d.authoredEnabled ? 1 : 0, latent = d.authoredEnabled ? 1 : 0, emitted = d.authoredEnabled ? 1 : 0 });
                                }

                                int parameter = t.index >= 0 ? t.index + 1 : int.TryParse(t.parameter, out int parsed) ? parsed : -1;
                                for (int x = 0; x < d.count; x++)
                                    if (parameters[d.parameters + x].ordinal == parameter)
                                        target = d.parameters + x;
                            }
                        }
                        else
                        {
                            var p = new TrackerDeviceParameter
                            {
                                device = -1,
                                track = ti,
                                instrument = -1,
                                chain = -1,
                                latent = 0,
                                emitted = 0,
                                authored = 0,
                                min = 0,
                                max = 1
                            };
                            if (t.kind == ParameterKind.InstrumentMacro && ids.TryGetValue(t.instrumentId, out int ii) && t.index >= 0 && t.index < 8)
                            {
                                p.kind = 1;
                                p.instrument = ii;
                                p.parameter = t.index;
                                p.authored = p.latent = p.emitted = state.macros[ii * 8 + t.index].authored;
                            }
                            else if (t.kind == ParameterKind.Mixer)
                            {
                                p.kind = 4;
                                p.parameter = t.parameter == "preVolume" ? 0 : t.parameter == "prePan" ? 1 : t.parameter == "postVolume" || t.parameter == "volume" ? 2 : t.parameter == "postPan" || t.parameter == "pan" ? 3 : -1;
                                var tr = state.tracks[ti];
                                p.authored = p.latent = p.emitted = p.parameter == 0 ? tr.preGain : p.parameter == 1 ? (tr.prePan + 1) * .5f : p.parameter == 2 ? tr.postGain : (tr.postPan + 1) * .5f;
                            }
                            else if (timing)
                            {
                                p.kind = 5;
                                p.parameter = t.parameter == "bpm" ? 0 : t.parameter == "linesPerBeat" ? 1 : t.parameter == "ticksPerLine" ? 2 : -1;
                                p.min = p.parameter == 0 ? 32 : 1;
                                p.max = p.parameter == 0 ? 999 : p.parameter == 1 ? 256 : 16;
                                p.authored = (float)(((p.parameter == 0 ? song.bpm : p.parameter == 1 ? song.linesPerBeat : song.ticksPerLine) - p.min) / (p.max - p.min));
                                p.latent = p.emitted = p.authored;
                            }

                            if (p.kind > 0 && p.parameter >= 0)
                            {
                                target = parameters.Count;
                                parameters.Add(p);
                            }
                        }

                        if (target < 0)
                        {
                            diagnostics.Add("AUTOMATION_TARGET_UNRESOLVED pattern=" + pi + " lane=" + lane.id);
                            continue;
                        }

                        var a = new TrackerAutomation
                        {
                            pattern = pi,
                            target = target,
                            points = knots.Count,
                            count = sorted.Count,
                            linear = lane.interpolation == AutomationInterpolation.Linear,
                            timing = timing
                        };
                        foreach (var point in sorted)
                            knots.Add(new TrackerAutomationPoint { line = point.line, value = point.value });
                        lanes.Add(a);
                    }
                }

            state.routeQueue = Buffer<int>(state.trackCount);
            state.routeMarks = Buffer<int>(state.trackCount);
            state.devices = Native(devices);
            state.deviceCount = devices.Count;
            state.deviceParameters = Native(parameters);
            state.deviceParameterCount = parameters.Count;
            state.deviceDirty = Buffer<byte>(devices.Count);
            state.parameterDirty = Buffer<byte>(parameters.Count);
            state.automation = Native(lanes);
            state.automationCount = lanes.Count;
            state.automationPoints = Native(knots);
        }
    }
}
