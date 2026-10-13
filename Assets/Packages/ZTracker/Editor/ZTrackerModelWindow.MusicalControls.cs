using System;
using System.Linq;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // UI units are deliberately independent of the saved physical parameter units.
        // Reading a legacy value never clamps or rewrites it; only an explicit edit does.
        struct MusicalRange
        {
            public string label, tip;
            // Explicit musical unit; presentation never has to guess it from a translated label.
            public string unit;
            public float min, max, scale;
            public int decimals;
            public bool logarithmic, unbounded;
        }

        static MusicalRange Musical(string field)
        {
            var r = new MusicalRange { label = FieldLabel(field), tip = "Amount applied to each playing note.", min = 0, max = 100, scale = 100, decimals = 0 };
            switch (field)
            {
                case "volume": case "level": r.label = field == "volume" ? "Volume %" : "Level %"; r.max = 200; r.tip = "Linear amplitude: 100% is unity gain, 0% is silent."; break;
                case "pan": r.label = "Pan %"; r.min = -100; r.tip = "Stereo position: -100% left, 0 centre, +100% right."; break;
                case "pulseWidth": r.label = "Pulse %"; r.min = 1; r.max = 99; r.tip = "Pulse wave duty cycle; 50% produces a square wave."; break;
                case "blend": r.label = "Blend %"; r.tip = "Amount of oscillator B mixed or modulated into oscillator A."; break;
                case "unisonSpread": r.label = "Spread %"; r.tip = "Stereo width of the unison voices."; break;
                case "vibratoRandomness": r.label = "Vibrato random %"; r.tip = "Random variation of vibrato speed; zero gives an even vibrato."; break;
                case "fineTune": case "fineTuneB": case "fineTuneBCents": r.label = "Tune cents"; r.min = -1200; r.max = 1200; r.scale = 1; r.tip = "Pitch offset in cents; 100 cents is one semitone."; break;
                case "transpose": r.label = "Tune steps"; r.min = -48; r.max = 48; r.scale = 1; r.tip = "Pitch offset in semitone steps, added to Tune cents. Twelve steps is one octave."; break;
                case "baseNote": case "baseNoteB": r.label = "Base note"; r.max = 119; r.scale = 1; r.tip = "MIDI note number played at the sample's original pitch."; break;
                case "unisonVoices": r.label = "Unison"; r.min = 1; r.max = 8; r.scale = 1; r.tip = "Detuned copies of the oscillator stacked inside ONE note, for a thicker sound. This is not polyphony: how many notes overlap is set by New note."; break;
                case "unisonDetune": r.label = "Detune cents"; r.max = 100; r.scale = 1; r.tip = "Unison pitch spread in cents; 100 cents is one semitone."; break;
                case "vibratoDepth": r.label = "Vibrato cents"; r.max = 100; r.scale = 1; r.tip = "Vibrato pitch excursion in cents; zero disables vibrato."; break;
                case "vibratoRate": r.label = "Vibrato Hz"; r.min = .1f; r.max = 12; r.scale = 1; r.decimals = 1; r.tip = "Vibrato cycles per second."; break;
                case "rate": r.label = "Rate Hz"; r.min = .01f; r.max = 20; r.scale = 1; r.decimals = 2; r.tip = "LFO cycles per second."; break;
                case "vibratoFadeIn": r.label = "Vibrato fade ms"; r.max = 2000; r.scale = 1000; r.tip = "Time after each note starts for vibrato to reach full depth."; break;
                case "glideSeconds": r.label = "Glide ms"; r.max = 2000; r.scale = 1000; r.tip = "Time to slide from the previous note to the new pitch."; break;
                case "instFilterCutoff": r.label = "Cutoff Hz"; r.min = 20; r.max = Math.Max(20, AudioSettings.outputSampleRate * .5f); r.scale = Math.Max(1, AudioSettings.outputSampleRate * .5f); r.logarithmic = true; r.tip = "Filter cutoff frequency. The logarithmic track gives equal space to each octave."; break;
                case "instFilterResonance": r.label = "Resonance Q"; r.min = .1f; r.max = 10; r.scale = 1; r.decimals = 2; r.tip = "Filter resonance Q: higher values emphasise the cutoff frequency."; break;
                case "instDelaySend": r.label = "Delay %"; r.tip = "Send level to the first Delay send bus declared in Mixer."; break;
                case "instReverbSend": r.label = "Reverb %"; r.tip = "Send level to the first Reverb send bus declared in Mixer."; break;
                case "freqRatio": case "waveBRatio": r.label = "Ratio ×"; r.min = .125f; r.max = 16; r.scale = 1; r.decimals = 2; r.tip = "Frequency relative to the played note; 2 is one octave higher."; break;
                case "freqFixed": r.label = "Fixed Hz"; r.max = 20000; r.scale = 1; r.tip = "Fixed frequency in Hz; zero follows the played note and ratio."; break;
                case "fmFeedback": r.label = "Output feedback"; r.max = 8; r.scale = 1; r.decimals = 2; r.tip = "Older whole-instrument feedback: the previous output is fed back into the first operator, from clean sine to a brighter, noisier tone. New sounds use each modulator's own Feedback instead."; break;
                case "feedback": r.label = "Feedback %"; r.tip = "This modulator bends its own phase with its previous output: 0% is a pure sine, higher values brighten it towards a sawtooth-like tone. Changes glide over 5 ms, so moving it while a note plays does not click."; break;
                case "pmDepth": r.label = "PM cycles"; r.max = 8; r.scale = 1; r.decimals = 2; r.tip = "Phase modulation depth in oscillator cycles."; break;
                case "phase": r.label = "Phase °"; r.max = 360; r.scale = 360; r.tip = "Initial LFO position in one cycle."; break;
                case "attack": case "blendAttack": case "hold": r.label = field == "hold" ? "Hold ms" : "Attack ms"; r.max = 5000; r.scale = 1000; r.tip = "Envelope stage duration."; break;
                case "decay": case "blendDecay": case "release": case "blendRelease": case "duration": r.label = field.Contains("ecay") ? "Decay ms" : field == "duration" ? "Duration ms" : "Release ms"; r.max = 10000; r.scale = 1000; r.tip = "Envelope stage duration."; break;
                case "sustain": case "blendSustain": r.label = "Sustain %"; r.tip = "Level held until the note is released."; break;
                case "curve": r.label = "Exponent"; r.min = .05f; r.max = 8; r.scale = 1; r.decimals = 2; r.tip = "Curve bend; 1 is linear, below 1 rises early, above 1 rises late."; break;
                case "depth": case "min": case "max": r.scale = 1; r.decimals = 2; r.unbounded = true; r.tip = "Physical output amount in the selected target's units. Negative values invert modulation where meaningful."; break;
            }
            switch (field)
            {
                case "volume": case "level": case "pan": case "pulseWidth": case "blend": case "unisonSpread": case "vibratoRandomness": case "feedback": r.unit = "%"; break;
                case "fineTune": case "fineTuneB": case "fineTuneBCents": case "unisonDetune": case "vibratoDepth": r.unit = "cents"; break;
                case "vibratoRate": case "rate": case "instFilterCutoff": case "freqFixed": r.unit = "Hz"; break;
                case "vibratoFadeIn": case "glideSeconds": r.unit = "ms"; break;
                case "instFilterResonance": r.unit = "Q"; break;
                case "freqRatio": case "waveBRatio": r.unit = "×"; break;
                case "pmDepth": r.unit = "cycles"; break;
                default: r.unit = ""; break;
            }
            return r;
        }

        static float DeclaredDefault(object owner, string field)
        {
            var definition = Activator.CreateInstance(owner.GetType());
            return Convert.ToSingle(owner.GetType().GetField(field).GetValue(definition));
        }
        float ParameterDefault(string field, string key)
        {
            var defaults = NewInstrumentData().parameters;
            if (key.StartsWith("fmOperators.", StringComparison.Ordinal))
            {
                int index = int.Parse(key.Split('.')[1]);
                return Convert.ToSingle(typeof(ZTrackerInstrument.FMOperatorData).GetField(field).GetValue(defaults.fmOperators[index]));
            }
            if (Instrument.family == InstrumentFamily.Sampler && field == "volume") return new SamplerData().volume;
            return Convert.ToSingle(typeof(InstrumentParameters).GetField(field).GetValue(defaults));
        }
        static ZuiAdsr.Options EnvelopeDefaults(object definition, string prefix = "")
        {
            float Read(string stage) { string field=prefix.Length==0?stage:prefix+char.ToUpperInvariant(stage[0])+stage.Substring(1);return Convert.ToSingle(definition.GetType().GetField(field)?.GetValue(definition)??0f); }
            return new ZuiAdsr.Options { attackDefault=Read("attack"),holdDefault=Read("hold"),decayDefault=Read("decay"),sustainDefault=Read("sustain"),releaseDefault=Read("release") };
        }
        VisualElement MusicalScalar(string field, float value, Action<float> apply, string name, float? defaultValue = null)
        {
            var r = Musical(field);
            if (r.unbounded) return InstrumentNumber(r.label, value, r.tip, apply, name);
            if (field == "unisonVoices" || field == "baseNote" || field == "baseNoteB")
                return Named(Z.ScrubNumber(r.label, Mathf.RoundToInt(value), Mathf.RoundToInt(r.min), Mathf.RoundToInt(r.max), r.tip, v => InstrumentEdit(r.label, () => apply(v)), 100, field == "unisonVoices" ? new InstrumentParameters().unisonVoices : new SampleData().baseNote), name);
            var control = Z.MicroSlider(r.label, value * r.scale, r.min, r.max, r.tip, v => InstrumentEdit(r.label, () => apply(v / r.scale)), 170, defaultValue: (defaultValue ?? ParameterDefault(field, field)) * r.scale, decimals: r.decimals, logarithmic: r.logarithmic);
            return Named(control, name);
        }

        static bool CanEnvelope(string field)
        {
            switch (field)
            {
                case "volume": case "pan": case "fineTune": case "instFilterCutoff": case "instFilterResonance":
                case "blend": case "pulseWidth": case "waveBRatio": case "pmDepth": case "unisonDetune": case "unisonSpread":
                case "vibratoDepth": case "vibratoRate": case "vibratoFadeIn": case "fmFeedback": return true;
                default: return false;
            }
        }

        static ZUIEnvelopeData PresetEnvelope(ZTrackerInstrument.InstrumentPreset preset, string key)
        {
            var field = preset.GetType().GetField(key + "EnvelopeData");
            return field != null ? (ZUIEnvelopeData)field.GetValue(preset) : preset.parameterEnvelopes?.Find(e => e != null && e.parameter == key)?.envelope;
        }

        static void SetPresetEnvelope(ZTrackerInstrument.InstrumentPreset preset, string key, ZUIEnvelopeData envelope)
        {
            var field = preset.GetType().GetField(key + "EnvelopeData");
            if (field != null) { field.SetValue(preset, envelope); return; }
            if (preset.parameterEnvelopes == null) preset.parameterEnvelopes = new System.Collections.Generic.List<ParameterEnvelope>();
            var entry = preset.parameterEnvelopes.Find(e => e != null && e.parameter == key);
            if (entry == null) { entry = new ParameterEnvelope { parameter = key }; preset.parameterEnvelopes.Add(entry); }
            entry.envelope = envelope;
        }

        VisualElement ParameterValue(string field, float value, Action<float> apply, string name, InstrumentParameters parameters, string key)
        {
            var control = ParameterValue(field, value, apply, name, () => parameters.GetParameterEnvelope(key), env => parameters.SetParameterEnvelope(key, env), defaultValue: ParameterDefault(field, key), parameterKey: key);
            // The instrument's own typed parameter: right-click also offers a command id when this instrument is in the song.
            var asset = instrument;
            if (asset != null && Data != null && Data.instruments.Contains(asset) && ReferenceEquals(asset.model?.parameters, parameters) && Laubrary.ZTracker.Engine.TrackerParameters.Resolve(key, out var parameter))
                CommandMenu(control, () => InstrumentTarget(asset, parameter));
            return control;
        }

        VisualElement ParameterValue(string field, float value, Action<float> apply, string name, Func<ZUIEnvelopeData> getEnvelope, Action<ZUIEnvelopeData> setEnvelope, MusicalRange? presentation = null, float? defaultValue = null, string parameterKey = null, bool playheads = true)
        {
            var r = presentation ?? Musical(field);
            var original = getEnvelope();
            bool rows = original != null && original.durationInRows;
            var ui = new ZUIValue(value * r.scale) { mode = original != null && original.enabled && original.points != null && original.points.Count > 0 ? ZUIValue.Mode.Curve : ZUIValue.Mode.Static, yMin = r.min, yMax = r.max, duration = original == null ? 1 : Math.Max(.001f, original.xMax) };
            if (original != null && original.points != null && original.points.Count > 0)
                foreach (var p in original.points) ui.points.Add(new ZUIEnvelopePoint(p.time / ui.duration, p.value * r.scale, p.exponent));
            else
            {
                ui.points.Add(new ZUIEnvelopePoint(0, value * r.scale, 1));
                ui.points.Add(new ZUIEnvelopePoint(1, value * r.scale, 1));
            }
            var host = new VisualElement(); host.style.alignSelf = Align.FlexStart; host.style.flexShrink = 0; host.style.maxWidth = Length.Percent(100);
            ZuiValueControl control = null;
            float lastStatic = ui.staticValue;
            void Publish()
            {
                if (ui.staticValue != lastStatic) { apply(ui.staticValue / r.scale); lastStatic = ui.staticValue; }
                if (getEnvelope() == null && ui.mode == ZUIValue.Mode.Static) { EndInstrumentGesture(); return; }
                var env = getEnvelope()?.DeepCopy() ?? new ZUIEnvelopeData(0, ui.duration, r.min / r.scale, r.max / r.scale, false) { sustainPosition = ui.duration * .5f };
                env.enabled = ui.mode == ZUIValue.Mode.Curve;
                if (env.enabled)
                {
                    // Repair a malformed list only in this detached copy, after an explicit edit.
                    if (env.points == null) env = new ZUIEnvelopeData(env.xMin, env.xMax, env.yMin, env.yMax, false) { enabled = true, requiresEndPoint = env.requiresEndPoint, loopEnabled = env.loopEnabled, loopMode = env.loopMode, loopStart = env.loopStart, loopEnd = env.loopEnd, sustainEnabled = env.sustainEnabled, sustainPosition = env.sustainPosition, durationInRows = env.durationInRows };
                    float durationRatio = ui.duration / Math.Max(.001f, env.xMax);
                    env.loopStart *= durationRatio; env.loopEnd *= durationRatio; env.sustainPosition *= durationRatio;
                    env.requiresEndPoint = false;
                    env.xMin = 0; env.xMax = ui.duration; env.yMin = r.min / r.scale; env.yMax = r.max / r.scale;
                    env.points.Clear();
                    foreach (var p in ui.points) env.points.Add(new ZUIEnvelopePoint(p.time * ui.duration, p.value / r.scale, p.exponent));
                    ClampMarkers(env);
                }
                // Switching to Static keeps the curve available for a later switch back.
                setEnvelope(env); EndInstrumentGesture(); RefreshSynthConditionalControls(rootVisualElement);
                ShowMarkers(control?.CurveEnvelope);
            }
            float Last(ZUIEnvelopeData env) => env.points == null || env.points.Count == 0 ? env.xMax : env.points.Max(p => p.time);
            void ClampMarkers(ZUIEnvelopeData env)
            {
                // The renderer refuses a loop or a hold past the last point; keep them on the curve.
                float last = Last(env);
                env.sustainPosition = Mathf.Clamp(env.sustainPosition, 0, last);
                env.loopEnd = Mathf.Clamp(env.loopEnd, 0, last);
                env.loopStart = Mathf.Clamp(env.loopStart, 0, Math.Max(0, env.loopEnd - .0001f));
                if (env.loopEnd <= env.loopStart) env.loopEnabled = false;
            }
            int loopOffset = Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring ? 1 : 0;
            // The loop band and the hold marker drawn on the curve, from the saved envelope.
            void ShowMarkers(ZuiEnvelope canvas)
            {
                if (canvas == null) return; var env = getEnvelope(); float d = env == null ? 1 : Math.Max(.0001f, env.xMax);
                canvas.SetLoop(env != null && env.loopEnabled, env == null ? 0 : env.loopStart / d, env == null ? 1 : env.loopEnd / d, env != null && env.loopMode - loopOffset == 1);
                canvas.SetSustain(env != null && env.sustainEnabled, env == null ? 0 : env.sustainPosition / d);
            }
            string Ticks(float f) => EnvelopeTimeText(f * ui.duration, rows);
            void Decorate(ZuiEnvelope canvas)
            {
                canvas.name = name + "-curve";
                ShowMarkers(canvas);
                var c = canvas.configuration;
                c.onMarkerDragStarted = marker => BeginInstrumentGesture(marker == 2 ? r.label + " sustain point" : r.label + " loop point");
                c.onMarkerMoved = (marker, t01) =>
                {
                    var env = getEnvelope()?.DeepCopy(); if (env == null) return;
                    float at = t01 * env.xMax; if (env.durationInRows) at = Mathf.Round(at);
                    if (marker == 0) env.loopStart = Math.Min(at, env.loopEnd - (env.durationInRows ? 1 : .0001f)); else if (marker == 1) env.loopEnd = Math.Max(at, env.loopStart + (env.durationInRows ? 1 : .0001f)); else env.sustainPosition = at;
                    env.loopStart = Math.Max(0, env.loopStart);
                    ClampMarkers(env); setEnvelope(env); EndInstrumentGesture(); ShowMarkers(canvas);
                };
                c.onMarkerDragEnded = () => ShowMarkers(canvas);
                c.onEmptyContext = world => ShowEnvelopeMenu(canvas, getEnvelope, setEnvelope, r.label);
                if (!playheads) return;
                // One playhead per sounding note of this instrument, read from the renderers' fixed records ~30 times a second.
                string key = parameterKey ?? field; if (!Laubrary.ZTracker.Engine.TrackerParameters.Resolve(key, out var parameter)) return;
                var positions = new float[64]; var faded = new bool[64]; var normalized = new float[64];
                canvas.schedule.Execute(() =>
                {
                    var env = getEnvelope(); int count = env == null || !env.enabled ? 0 : ReadPlayheads(instrument, (int)parameter, positions, faded);
                    float d = env == null ? 1 : Math.Max(.0001f, env.xMax);
                    for (int i = 0; i < count; i++) normalized[i] = positions[i] / d;
                    canvas.SetPlayheads(normalized, faded, count);
                }).Every(33);
            }
            control = Z.Value(r.label, ui, new ZuiValueControl.Options { absMin = r.min, absMax = r.max, staticDefault = (defaultValue ?? ParameterDefault(field, field)) * r.scale, decimals = r.decimals, logarithmic = r.logarithmic, controlWidth = 170, allowMinMax = false, allowSteps = false, allowOscillation = false, preserveCurveOnModeSwitch = true, hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true, valueUnit = r.unit ?? "", xAxisLabel = rows ? "Song rows" : "Seconds", yAxisLabel = r.label, yAxisLegend = true, showAxisLegends = EnvelopeAxisLegends, xTickLabel = Ticks, decorateEnvelope = Decorate },
                r.tip + " Right-click for a per-note envelope. Every new note restarts it; held loops leave on note-off. On the curve: drag the loop and hold markers; right-click the curve's background for its display options.", Publish, () => BeginInstrumentGesture(r.label));
            control.name = name;
            host.Add(control);
            var timing = Flow(); timing.name = name + "-timing";
            float defaultDuration = rows ? EnvelopeDefaultRows : new ZUIEnvelopeData().xMax;
            timing.Add(Named(rows
                ? Z.MicroSlider("Duration rows", ui.duration, 1, 256, "Length of one envelope pass in song rows, so it follows the song's tempo; each new note starts at zero. Loop and hold positions scale with it. Double-click resets to " + EnvelopeDefaultRows + " rows.", v => { BeginInstrumentGesture(r.label + " duration"); ui.duration = Mathf.Max(1, Mathf.Round(v)); Publish(); }, 150, defaultValue: defaultDuration, decimals: 0, logarithmic: true)
                : Z.MicroSlider("Duration s", ui.duration, .01f, 10, "Length of one envelope pass in seconds; each new note starts at zero. Loop and hold positions scale with it. Double-click resets to " + defaultDuration + " s.", v => { BeginInstrumentGesture(r.label + " duration"); ui.duration = v; Publish(); }, 150, defaultValue: defaultDuration, decimals: 2, logarithmic: true), name + "-duration"));
            timing.Add(InstrumentChoice("Time in", rows ? 1 : 0, new[] { "Seconds", "Rows" }, "Measure the envelope in seconds, or in song rows so it follows the song's tempo (a tempo change speeds it up or slows it down). Switching keeps its length at the song's current tempo.",
                v => { var env = getEnvelope()?.DeepCopy(); if (env == null || env.durationInRows == (v == 1)) return; ConvertEnvelopeUnit(env, v == 1); setEnvelope(env); }, name + "-unit"));
            timing.Add(InstrumentToggle("Loop", original != null && original.loopEnabled, "Repeat part of the envelope while the note is held: the shaded band on the curve, between its two markers. Note-off leaves the loop.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; float last = Last(env); env.loopEnabled = v && last > 0; if (env.loopEnd <= env.loopStart || env.loopEnd > last) { env.loopStart = 0; env.loopEnd = last; } if (env.loopMode == 0 && Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring) env.loopMode = 1; setEnvelope(env); }, name + "-loop", true));
            if (original != null && original.loopEnabled)
                timing.Add(InstrumentChoice("Direction", Math.Max(0, original.loopMode - loopOffset), new[] { "Forward", "Ping-pong" }, "Forward jumps back to the loop start at its end; Ping-pong runs back and forth. The envelope continues forward after note-off.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; env.loopMode = v + loopOffset; setEnvelope(env); }, name + "-loop-direction"));
            timing.Add(InstrumentToggle("Sustain", original != null && original.sustainEnabled, "Hold the envelope at the dashed S marker on the curve until note-off, then play the rest of it.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; env.sustainEnabled = v; if (v && (env.sustainPosition <= 0 || env.sustainPosition > Last(env))) env.sustainPosition = Last(env) * .5f; env.sustainPosition = Mathf.Clamp(env.sustainPosition, 0, Last(env)); setEnvelope(env); }, name + "-sustain", true));
            host.Add(timing);
            void LayoutMode() { bool curve = ui.mode == ZUIValue.Mode.Curve; bool expanded = curve && control.IsExpanded; timing.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None; host.style.width = expanded ? 380 : curve ? 260 : 170; }
            control.ModeChanged += LayoutMode; control.ExpansionChanged += LayoutMode; LayoutMode();
            return host;
        }

        const float EnvelopeDefaultRows = 8;
        const string AxisLegendsPref = "ZTracker.EnvelopeAxisLegends";
        static bool EnvelopeAxisLegends { get => UnityEditor.EditorPrefs.GetBool(AxisLegendsPref, true); set => UnityEditor.EditorPrefs.SetBool(AxisLegendsPref, value); }

        /// "250 ms", "1.5 s" or "4 rows": a position on an envelope's time axis.
        static string EnvelopeTimeText(float time, bool rows)
        {
            if (rows) { float r = Mathf.Round(time * 10) / 10; return r.ToString(r == Mathf.Round(r) ? "0" : "0.#") + (Mathf.Abs(r - 1) < .001f ? " row" : " rows"); }
            return time < 1 ? Mathf.RoundToInt(time * 1000) + " ms" : time.ToString(time < 10 ? "0.##" : "0.#") + " s";
        }

        /// Seconds and rows at the song's tempo (120 BPM and 4 lines per beat without a song).
        double RowsPerSecond => Data == null ? 8 : Data.bpm * Math.Max(1, Data.linesPerBeat) / 60.0;

        /// Switches an envelope between seconds and song rows, keeping its length at the current tempo.
        void ConvertEnvelopeUnit(ZUIEnvelopeData env, bool toRows)
        {
            float factor = (float)(toRows ? RowsPerSecond : 1 / RowsPerSecond);
            float duration = Math.Max(toRows ? 1 : .01f, env.xMax * factor); if (toRows) duration = Mathf.Max(1, Mathf.Round(duration));
            float scale = duration / Math.Max(.0001f, env.xMax);
            if (env.points != null) foreach (var p in env.points) p.time *= scale;
            env.loopStart *= scale; env.loopEnd *= scale; env.sustainPosition *= scale; env.xMax = duration; env.durationInRows = toRows;
        }

        /// The envelope's own context menu (right-click on the curve's background): display options.
        void ShowEnvelopeMenu(VisualElement anchor, Func<ZUIEnvelopeData> getEnvelope, Action<ZUIEnvelopeData> setEnvelope, string label)
        {
            var menu = Z.Menu(anchor).Width(200).Section(label + " envelope");
            menu.Toggle("Axis legends", "Show what the axes measure: time (seconds or song rows) along the bottom and the value up the side, on every instrument envelope.", EnvelopeAxisLegends, v =>
            {
                EnvelopeAxisLegends = v;
                rootVisualElement.Query<ZuiValueControl>().ForEach(c => { var e = c.CurveEnvelope; if (e != null && e.name != null && e.name.EndsWith("-curve", StringComparison.Ordinal)) c.SetAxisLegends(v); });
            });
            menu.Show();
        }

        readonly float[] playheadPart = new float[64]; readonly bool[] playheadPartFaded = new bool[64];
        /// Where each sounding note of <paramref name="asset"/> is on its envelope for <paramref name="parameter"/>, from
        /// every renderer this window owns (the song's and the instrument preview's).
        int ReadPlayheads(ZTrackerInstrument asset, int parameter, float[] positions, bool[] faded)
        {
            if (asset == null) return 0; int count = 0; var part = playheadPart; var partFaded = playheadPartFaded;
            void From(Laubrary.ZTracker.Engine.TrackerSapGenerator renderer, int index)
            {
                if (renderer == null || index < 0) return;
                int n = renderer.ReadEnvelopeHeads(index, parameter, -1, part, partFaded);
                for (int i = 0; i < n && count < positions.Length; i++) { positions[count] = part[i]; faded[count] = partFaded[i]; count++; }
            }
            From(host, Data == null ? -1 : Data.instruments.IndexOf(asset));
            if (midiPreviewInstrument == asset) From(midiPreviewHost, 0);
            return count;
        }

        VisualElement FixedAdsr(string title, object owner, string prefix = "", string name = null)
        {
            string Field(string stage) => prefix.Length == 0 ? stage : prefix + char.ToUpperInvariant(stage[0]) + stage.Substring(1);
            float Read(string stage) => Convert.ToSingle(owner.GetType().GetField(Field(stage)).GetValue(owner));
            void Write(string stage, float value) => InstrumentEdit((prefix.Length > 0 ? prefix + " " : "envelope ") + stage, () => owner.GetType().GetField(Field(stage)).SetValue(owner, value));
            var hold = owner.GetType().GetField(Field("hold"));
            var reset = EnvelopeDefaults(Activator.CreateInstance(owner.GetType()), prefix);
            var control = hold == null
                ? Z.Adsr(title, () => Read("attack"), v => Write("attack", v), () => Read("decay"), v => Write("decay", v), () => Read("sustain"), v => Write("sustain", v), () => Read("release"), v => Write("release", v), "Attack, decay, sustain and release. Drag a point or a slider; double-click resets a stage.", reset)
                : Z.Adsr(title, () => Read("attack"), v => Write("attack", v), () => Read("hold"), v => Write("hold", v), () => Read("decay"), v => Write("decay", v), () => Read("sustain"), v => Write("sustain", v), () => Read("release"), v => Write("release", v), "Attack, hold, decay, sustain and release. Drag a point or a slider; double-click resets a stage. Hold keeps the peak before decay begins.", reset);
            control.style.maxWidth = Length.Percent(100);
            return Named(control, name);
        }

        ZuiBox InstrumentSection(VisualElement root, string title, string key, string tip)
        {
            var box = Z.BoxKeyed(title, tip, "tracker.instrument." + key);
            box.style.minWidth = 0; box.style.flexShrink = 1;
            box.contentContainer.style.minWidth = 0;
            root.Add(box);
            return box;
        }
    }
}
