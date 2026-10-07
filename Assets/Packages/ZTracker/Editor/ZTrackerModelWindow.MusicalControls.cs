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
                case "vibratoRandomness": r.label = "Random %"; r.tip = "Random variation of vibrato speed; zero gives an even vibrato."; break;
                case "fineTune": case "fineTuneB": case "fineTuneBCents": r.label = "Tune cents"; r.min = -1200; r.max = 1200; r.scale = 1; r.tip = "Pitch offset in cents; 100 cents is one semitone."; break;
                case "baseNote": case "baseNoteB": r.label = "Base note"; r.max = 119; r.scale = 1; r.tip = "MIDI note number played at the sample's original pitch."; break;
                case "unisonVoices": r.label = "Unison"; r.min = 1; r.max = 8; r.scale = 1; r.tip = "Detuned copies of the oscillator stacked inside ONE note, for a thicker sound. This is not polyphony: how many notes overlap is set by New note."; break;
                case "unisonDetune": r.label = "Detune cents"; r.max = 100; r.scale = 1; r.tip = "Unison pitch spread in cents; 100 cents is one semitone."; break;
                case "vibratoDepth": r.label = "Depth cents"; r.max = 100; r.scale = 1; r.tip = "Vibrato pitch excursion in cents; zero disables vibrato."; break;
                case "vibratoRate": r.label = "Rate Hz"; r.min = .1f; r.max = 12; r.scale = 1; r.decimals = 1; r.tip = "Vibrato cycles per second."; break;
                case "rate": r.label = "Rate Hz"; r.min = .01f; r.max = 20; r.scale = 1; r.decimals = 2; r.tip = "LFO cycles per second."; break;
                case "vibratoFadeIn": r.label = "Fade ms"; r.max = 2000; r.scale = 1000; r.tip = "Time after each note starts for vibrato to reach full depth."; break;
                case "glideSeconds": r.label = "Glide ms"; r.max = 2000; r.scale = 1000; r.tip = "Time to slide from the previous note to the new pitch."; break;
                case "instFilterCutoff": r.label = "Cutoff Hz"; r.min = 20; r.max = Math.Min(20000, AudioSettings.outputSampleRate * .49f); r.scale = Math.Max(1, AudioSettings.outputSampleRate * .5f); r.logarithmic = true; r.tip = "Filter cutoff frequency. The logarithmic track gives equal space to each octave."; break;
                case "instFilterResonance": r.label = "Resonance Q"; r.min = .1f; r.max = 10; r.scale = 1; r.decimals = 2; r.tip = "Filter resonance Q: higher values emphasise the cutoff frequency."; break;
                case "instDelaySend": r.label = "Delay %"; r.tip = "Send level to the first Delay send bus declared in Mixer."; break;
                case "instReverbSend": r.label = "Reverb %"; r.tip = "Send level to the first Reverb send bus declared in Mixer."; break;
                case "freqRatio": case "waveBRatio": r.label = "Ratio ×"; r.min = .125f; r.max = 16; r.scale = 1; r.decimals = 2; r.tip = "Frequency relative to the played note; 2 is one octave higher."; break;
                case "freqFixed": r.label = "Fixed Hz"; r.max = 20000; r.scale = 1; r.tip = "Fixed frequency in Hz; zero follows the played note and ratio."; break;
                case "fmFeedback": r.label = "Feedback"; r.max = 8; r.scale = 1; r.decimals = 2; r.tip = "Operator phase feedback, from clean sine to a brighter, noisier tone."; break;
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
                case "volume": case "level": case "pan": case "pulseWidth": case "blend": case "unisonSpread": case "vibratoRandomness": r.unit = "%"; break;
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

        VisualElement MusicalScalar(string field, float value, Action<float> apply, string name)
        {
            var r = Musical(field);
            if (r.unbounded) return InstrumentNumber(r.label, value, r.tip, apply, name);
            var control = Z.MicroSlider(r.label, value * r.scale, r.min, r.max, r.tip, v => InstrumentEdit(r.label, () => apply(v / r.scale)), 170, decimals: r.decimals, logarithmic: r.logarithmic);
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
            => ParameterValue(field, value, apply, name, () => parameters.GetParameterEnvelope(key), env => parameters.SetParameterEnvelope(key, env));

        VisualElement ParameterValue(string field, float value, Action<float> apply, string name, Func<ZUIEnvelopeData> getEnvelope, Action<ZUIEnvelopeData> setEnvelope, MusicalRange? presentation = null)
        {
            var r = presentation ?? Musical(field);
            var original = getEnvelope();
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
                    if (env.points == null) env = new ZUIEnvelopeData(env.xMin, env.xMax, env.yMin, env.yMax, false) { enabled = true, requiresEndPoint = env.requiresEndPoint, loopEnabled = env.loopEnabled, loopMode = env.loopMode, loopStart = env.loopStart, loopEnd = env.loopEnd, sustainEnabled = env.sustainEnabled, sustainPosition = env.sustainPosition };
                    float durationRatio = ui.duration / Math.Max(.001f, env.xMax);
                    env.loopStart *= durationRatio; env.loopEnd *= durationRatio; env.sustainPosition *= durationRatio;
                    env.requiresEndPoint = false;
                    env.xMin = 0; env.xMax = ui.duration; env.yMin = r.min / r.scale; env.yMax = r.max / r.scale;
                    env.points.Clear();
                    foreach (var p in ui.points) env.points.Add(new ZUIEnvelopePoint(p.time * ui.duration, p.value / r.scale, p.exponent));
                    float last = env.points.Count == 0 ? ui.duration : env.points.Max(p => p.time);
                    env.sustainPosition = Mathf.Clamp(env.sustainPosition, 0, last);
                    env.loopEnd = Mathf.Clamp(env.loopEnd, 0, last);
                    env.loopStart = Mathf.Clamp(env.loopStart, 0, Math.Max(0, env.loopEnd - .0001f));
                    if (env.loopEnd <= env.loopStart) env.loopEnabled = false;
                }
                // Switching to Static keeps the curve available for a later switch back.
                setEnvelope(env); EndInstrumentGesture();
            }
            control = Z.Value(r.label, ui, new ZuiValueControl.Options { absMin = r.min, absMax = r.max, decimals = r.decimals, logarithmic = r.logarithmic, controlWidth = 170, allowMinMax = false, allowSteps = false, allowOscillation = false, preserveCurveOnModeSwitch = true, hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true, valueUnit = r.unit ?? "", xAxisLabel = "Note progress", yAxisLabel = r.label }, r.tip + " Right-click for a per-note envelope. Every new note restarts it; held loops leave on note-off.", Publish, () => BeginInstrumentGesture(r.label));
            control.name = name;
            host.Add(control);
            var timing = Flow(); timing.name = name + "-timing";
            timing.Add(Named(Z.MicroSlider("Duration s", ui.duration, .01f, 10, "Duration of one envelope pass in seconds; each new note starts at zero. Loop and sustain positions scale with the duration.", v => { BeginInstrumentGesture(r.label + " duration"); ui.duration = v; Publish(); }, 170, decimals: 2, logarithmic: true), name + "-duration"));
            timing.Add(InstrumentToggle("Loop", original != null && original.loopEnabled, "Repeat the envelope while the note is held; note-off leaves the loop.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; float last = env.points.Select(p => p.time).DefaultIfEmpty(0).Max(); env.loopEnabled = v && last > 0; if (env.loopEnd <= env.loopStart || env.loopEnd > last) { env.loopStart = 0; env.loopEnd = last; } if (env.loopMode == 0 && Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring) env.loopMode = 1; setEnvelope(env); }, name + "-loop", true));
            int loopOffset = Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring ? 1 : 0;
            timing.Add(InstrumentChoice("Direction", original == null ? 0 : Math.Max(0, original.loopMode - loopOffset), new[] { "Forward", "Ping-pong" }, "Direction of the held loop. The envelope continues forward after note-off.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; env.loopMode = v + loopOffset; setEnvelope(env); }, name + "-loop-direction"));
            timing.Add(InstrumentToggle("Sustain", original != null && original.sustainEnabled, "Hold at the sustain point until note-off, then continue through the envelope tail.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; env.sustainEnabled = v; env.sustainPosition = Mathf.Clamp(env.sustainPosition, 0, env.points.Select(p => p.time).DefaultIfEmpty(0).Max()); setEnvelope(env); }, name + "-sustain", true));
            timing.Add(InstrumentDial("Sustain %", original == null ? 50 : original.sustainPosition / ui.duration * 100, 0, 100, "Hold position as a percentage of envelope duration; the remaining curve plays after note-off.", v => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; float last = env.points.Select(p => p.time).DefaultIfEmpty(ui.duration).Max(); env.sustainPosition = Math.Min(last, ui.duration * v / 100); setEnvelope(env); }, name + "-sustain-position", 0));
            timing.Add(Named(Z.MicroMinMax("Loop %", original == null ? 0 : original.loopStart / ui.duration * 100, original == null ? 100 : original.loopEnd / ui.duration * 100, 0, 100, "Held loop start and end as percentages of the envelope duration.", (a, b) => InstrumentEdit("envelope loop range", () => { var env = getEnvelope()?.DeepCopy(); if (env == null) return; float last = env.points.Select(p => p.time).DefaultIfEmpty(ui.duration).Max(); env.loopEnd = Math.Min(last, Math.Max(.0001f, ui.duration * b / 100)); env.loopStart = Math.Min(Math.Max(0, env.loopEnd - .0001f), ui.duration * a / 100); setEnvelope(env); }), 170), name + "-loop-range"));
            host.Add(timing);
            void LayoutMode() { bool curve = ui.mode == ZUIValue.Mode.Curve; bool expanded = curve && control.IsExpanded; timing.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None; host.style.width = expanded ? 380 : curve ? 260 : 170; }
            control.ModeChanged += LayoutMode; control.ExpansionChanged += LayoutMode; LayoutMode();
            return host;
        }

        VisualElement FixedAdsr(string title, object owner, string prefix = "", string name = null)
        {
            string Field(string stage) => prefix.Length == 0 ? stage : prefix + char.ToUpperInvariant(stage[0]) + stage.Substring(1);
            float Read(string stage) => Convert.ToSingle(owner.GetType().GetField(Field(stage)).GetValue(owner));
            void Write(string stage, float value) => InstrumentEdit(title + " " + stage, () => owner.GetType().GetField(Field(stage)).SetValue(owner, value));
            var hold = owner.GetType().GetField(Field("hold"));
            var control = hold == null
                ? Z.Adsr(title, () => Read("attack"), v => Write("attack", v), () => Read("decay"), v => Write("decay", v), () => Read("sustain"), v => Write("sustain", v), () => Read("release"), v => Write("release", v), "Drag a stage handle; exact time inputs are milliseconds and sustain is percent.")
                : Z.Adsr(title, () => Read("attack"), v => Write("attack", v), () => Read("hold"), v => Write("hold", v), () => Read("decay"), v => Write("decay", v), () => Read("sustain"), v => Write("sustain", v), () => Read("release"), v => Write("release", v), "Drag a stage handle; Hold keeps the peak before decay begins.");
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
