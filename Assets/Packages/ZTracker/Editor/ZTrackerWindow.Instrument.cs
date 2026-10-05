using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerWindow
    {
        static readonly string[] Waves = { "Sine", "Triangle", "Saw", "Square", "Noise" };
        int selectedKit;
        [SerializeField] List<string> kitViewKeys = new List<string>();
        [SerializeField] List<string> macroViewKeys = new List<string>();
        [SerializeField] string viewInstrument;
        void PrepareInstanceKeys()
        {
            string identity = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(instrument));
            if (identity != viewInstrument) { kitViewKeys.Clear(); macroViewKeys.Clear(); viewInstrument = identity; }
            while (kitViewKeys.Count < (instrument.kitEntries?.Length ?? 0)) kitViewKeys.Add(Guid.NewGuid().ToString("N"));
            while (macroViewKeys.Count < (instrument.macros?.Length ?? 0)) macroViewKeys.Add(Guid.NewGuid().ToString("N"));
        }
        static readonly string[] MacroParams = { "blend", "pulseWidth", "volume", "pan", "attack", "decay", "sustain", "release", "vibratoDepth", "vibratoRate", "pmDepth", "waveBRatio", "unisonDetune", "unisonSpread" };
        static readonly Dictionary<string, string[]> Choices = new Dictionary<string, string[]> {
            { "waveA", Waves }, { "waveB", Waves }, { "waveform", Waves }, { "blendMode", new[] { "Mix", "Ring", "Sync", "PM" } }, { "instFilterMode", new[] { "Low pass", "High pass", "Band pass" } },
            { "fmAlgorithm", new[] { "1", "2", "3", "4", "5", "6", "7", "8" } } };
        // Bounds match the existing tracker authoring surface or serialized Range metadata.
        static readonly Dictionary<string, Vector2> Ranges = new Dictionary<string, Vector2> {
            { "baseNote", new Vector2(0,126) }, { "baseNoteB", new Vector2(0,126) }, { "midiNote", new Vector2(0,126) }, { "fineTune", new Vector2(-100,100) }, { "fineTuneB", new Vector2(-100,100) },
            { "attack", new Vector2(0,5) }, { "decay", new Vector2(0,5) }, { "release", new Vector2(0,10) }, { "blendAttack", new Vector2(0,5) }, { "blendDecay", new Vector2(0,5) }, { "blendRelease", new Vector2(0,10) },
            { "blend", new Vector2(0,1) }, { "pulseWidth", new Vector2(.01f,.99f) }, { "waveBRatio", new Vector2(.1f,16) }, { "pmDepth", new Vector2(0,10) }, { "unisonDetune", new Vector2(0,100) },
            { "vibratoDepth", new Vector2(0,200) }, { "vibratoRate", new Vector2(0,20) }, { "unisonSpread", new Vector2(0,1) },
            { "vibratoFadeIn", new Vector2(0,5) }, { "arpeggioSpeed", new Vector2(.01f,1) }, { "volume", new Vector2(0,1) }, { "pan", new Vector2(-1,1) }, { "sustain", new Vector2(0,1) } };
        static string LabelFor(string name)
        {
            if (name.StartsWith("inst")) name = name.Substring(4);
            if (name.EndsWith("ClipB")) return "Sample B";
            if (name == "sampleClip") return "Sample A";
            return ObjectNames.NicifyVariableName(name);
        }
        static string TipFor(string name)
        {
            switch (name) {
                case "sampleClip": case "sampleClipB": case "clip": return "PCM source used by playback. Assign an audio clip; Decompress On Load is required for sample data.";
                case "baseNote": case "baseNoteB": return "MIDI pitch at which the sample plays at its original speed.";
                case "fineTune": case "fineTuneB": return "Pitch offset in cents; 100 cents is one semitone.";
                case "waveA": return "Primary oscillator shape."; case "waveB": return "Secondary oscillator shape."; case "waveform": return "Wave shape of this FM operator.";
                case "blendMode": return "How the A and B sources combine: mix, ring modulation, hard sync or phase modulation.";
                case "blend": return "Balance or modulation amount between source A and B."; case "pulseWidth": return "Square-wave duty cycle; 0.5 gives equal high and low halves.";
                case "waveBRatio": return "Source B frequency relative to source A."; case "pmDepth": return "Depth of phase modulation from source B.";
                case "volume": return "Output level of this instrument or kit sample."; case "pan": return "Stereo balance; -1 left, 0 center, 1 right.";
                case "attack": case "blendAttack": return "Seconds to rise from zero to peak after a note starts."; case "decay": case "blendDecay": return "Seconds to fall from peak to the sustain level.";
                case "sustain": case "blendSustain": return "Level held while the note remains on."; case "release": case "blendRelease": return "Seconds to fade out after note OFF.";
                case "unisonVoices": return "Number of detuned voices triggered by one synth note."; case "unisonDetune": return "Pitch spread of unison voices, in cents."; case "unisonSpread": return "Stereo width of unison voices.";
                case "vibratoDepth": return "Pitch modulation depth in cents."; case "vibratoRate": return "Pitch modulation cycles per second."; case "vibratoFadeIn": return "Seconds for vibrato to reach its full depth."; case "vibratoRandomness": return "Random variation in vibrato.";
                case "instFilterEnabled": return "Apply this instrument's filter to each voice."; case "instFilterMode": return "Choose frequencies passed by the filter."; case "instFilterCutoff": return "Normalized filter cutoff; higher passes higher frequencies."; case "instFilterResonance": return "Emphasize frequencies near the filter cutoff.";
                case "instDelaySend": return "Amount of this voice sent to delay."; case "instReverbSend": return "Amount of this voice sent to reverb.";
                case "fmAlgorithm": return "Routing between four FM operators; 1 is serial and 8 uses independent carriers."; case "fmFeedback": return "Feedback amount fed into the FM operator network.";
                case "freqRatio": return "Operator frequency as a multiple of the note's frequency."; case "freqFixed": return "Fixed operator frequency in Hz; zero uses the frequency ratio."; case "level": return "Operator output or modulation level.";
                case "kitOverlap": return "Allow drum hits on one track to ring over later hits."; case "midiNote": return "Piano note that triggers this kit sample."; case "displayName": return "Short drum name displayed in the pattern instead of its pitch.";
                case "arpeggioEnabled": return "Cycle the authored semitone offsets during a held note."; case "arpeggioSpeed": return "Seconds between arpeggio steps."; case "blendEnvelope": return "Use a separate ADSR for source blending.";
                default: return "Set " + LabelFor(name).ToLowerInvariant() + " for this sound. Changes are heard on the next audition.";
            }
        }
        VisualElement FieldControl(object owner, string name, Action<object> commit = null)
        {
            FieldInfo f = owner.GetType().GetField(name); object value = f.GetValue(owner);
            bool liveScalar = name == "blend" || name == "blendMode" || name == "waveA" || name == "waveB" || name == "pulseWidth" || name == "waveBRatio" || name == "pmDepth";
            Action<object> set = v => InstrumentEdit(LabelFor(name), () => { f.SetValue(owner, v); commit?.Invoke(owner); }, liveScalar: liveScalar);
            VisualElement control;
            if (Choices.TryGetValue(name, out var options)) { var choice = options.Length <= 3 ? (VisualElement)Z.Segmented((int)value, options, TipFor(name), v => set(v)) : Z.MiniRadio((int)value, options, TipFor(name), v => set(v), wrap: true); choice.style.maxWidth = 235; control = Z.Field(LabelFor(name), TipFor(name), choice); }
            else if (f.FieldType == typeof(bool)) control = Z.Toggle(LabelFor(name), TipFor(name), (bool)value, v => set(v));
            else if (f.FieldType == typeof(AudioClip)) control = Z.Field(LabelFor(name), TipFor(name), Z.Object((AudioClip)value, TipFor(name), v => { set(v); RebuildControls(); }, 235));
            else if (f.FieldType == typeof(string)) control = Z.Field(LabelFor(name), TipFor(name), Z.TextInput((string)value, TipFor(name), v => set(v), 140));
            else
            {
                var range = f.GetCustomAttribute<RangeAttribute>(); Vector2 bounds;
                bool bounded = range != null; bounds = bounded ? new Vector2(range.min, range.max) : Vector2.zero;
                if (!bounded) bounded = Ranges.TryGetValue(name, out bounds);
                bool integer = f.FieldType == typeof(int);
                if (bounded) control = Z.MicroSlider(LabelFor(name), Convert.ToSingle(value), bounds.x, bounds.y, TipFor(name), v => set(integer ? (object)Mathf.RoundToInt(v) : v), 145, decimals: integer ? 0 : 3);
                else control = Z.Field(LabelFor(name), TipFor(name), integer ? (VisualElement)Z.Int((int)value, TipFor(name), v => set(v), 90) : Z.Float((float)value, TipFor(name), v => set(Mathf.Max(0, v)), 90));
            }
            return Named(control, "instrument-" + name);
        }
        void BuildInstrument(VisualElement root)
        {
            root.Add(Flow(Named(Z.Object(instrument, "Pick a saved instrument to author or audition.", v => { StopPreview(); instrument = v; preset = -1; Rebuild(); }, 220), "instrument"), Button("New instrument", "Create a saved synth instrument; add it to the selected song.", CreateInstrument, "new-instrument")));
            if (instrument == null) return;
            PrepareInstanceKeys();
            root.Add(Flow(Button("Audition", "Play this preset at the current entry octave.", () => Audition(octave * 12), "audition"), Button("Stop", "Release the audition engine.", StopPreview), Button("Duplicate", "Create a separate saved copy and add it to the song.", () => { Folder(); var copy = Instantiate(instrument); copy.name = instrument.name + " copy"; Laubrary.ZTracker.Model.ZTrackerLegacyCompatibility.AssignDuplicateIdentity(copy); AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/" + copy.name + ".asset")); Undo.RegisterCreatedObjectUndo(copy, "Tracker: duplicate instrument"); instrument = copy; if (song != null) SongEdit("add instrument", () => song.instruments.Add(copy)); preset = -1; RebuildControls(); })));
            root.Add(Named(Z.MiniRadio((int)instrument.type, Enum.GetNames(typeof(InstrumentType)), "Choose the instrument engine; switching keeps the other engines' settings.", v => InstrumentEdit("instrument type", () => { instrument.type = (InstrumentType)v; InitializeFM(); }, true), wrap: true), "instrument-engine"));
            BuildPresets(root);
            Section(root, "Level", "ovrVolPan", "volume", "pan");
            if (instrument.type == InstrumentType.Sample)
            {
                Section(root, "Samples", "ovrSampleParams", "sampleClip", "baseNote", "fineTune", "sampleClipB", "baseNoteB", "fineTuneB");
                object sampleOwner = SectionOwner("ovrSampleParams");
                var a = (AudioClip)sampleOwner.GetType().GetField("sampleClip").GetValue(sampleOwner); var b = (AudioClip)sampleOwner.GetType().GetField("sampleClipB").GetValue(sampleOwner);
                if (a != null) root.Add(Waveform(a, "Source A waveform; click a piano key or Audition to hear it.")); if (b != null) root.Add(Waveform(b, "Source B waveform used by the blend modes."));
                Section(root, "Blend mode", null, "blendMode"); Parameter(root, "Blend", "blend", "blendEnvelopeData", "ovrBlend"); Parameter(root, "B ratio", "waveBRatio", "waveBRatioEnvelopeData", "ovrBRatio"); Parameter(root, "PM depth", "pmDepth", "pmDepthEnvelopeData", "ovrPMDepth");
            }
            if (instrument.type == InstrumentType.Synth)
            {
                Section(root, "Oscillators", "ovrSynthParams", "waveA", "waveB", "blendMode", "unisonVoices", "unisonSpread");
                Parameter(root, "Blend", "blend", "blendEnvelopeData", "ovrBlend"); Parameter(root, "Pulse width", "pulseWidth", "pulseWidthEnvelopeData", "ovrPulseWidth"); Parameter(root, "B ratio", "waveBRatio", "waveBRatioEnvelopeData", "ovrBRatio"); Parameter(root, "PM depth", "pmDepth", "pmDepthEnvelopeData", "ovrPMDepth"); Parameter(root, "Detune", "unisonDetune", "unisonDetuneEnvelopeData", "ovrDetune");
                Section(root, "Blend ADSR", null, "blendEnvelope", "blendAttack", "blendDecay", "blendSustain", "blendRelease");
            }
            if (instrument.type == InstrumentType.FM) BuildFM(root);
            if (instrument.type == InstrumentType.Kit) BuildKit(root);
            Section(root, "Amplitude", "ovrAdsr", "attack", "decay", "sustain", "release"); BuildADSR(root);
            Section(root, "Vibrato", "ovrVibrato", "vibratoDepth", "vibratoRate", "vibratoFadeIn", "vibratoRandomness");
            Section(root, "Effects", "ovrEffects", "instFilterEnabled", "instFilterMode", "instFilterCutoff", "instFilterResonance", "instDelaySend", "instReverbSend");
            BuildArpeggio(root); BuildMacros(root); BuildPiano(root);
        }
        object SectionOwner(string flag)
        {
            var p = instrument.GetPreset(preset); return p != null && flag != null && (bool)p.GetType().GetField(flag).GetValue(p) ? (object)p : instrument;
        }
        void OverrideHeader(VisualElement card, string flag, string[] fields)
        {
            var p = instrument.GetPreset(preset); if (p == null || flag == null) return; var f = p.GetType().GetField(flag); bool on = (bool)f.GetValue(p);
            card.Add(Flow(Z.Toggle("Override", on ? "Use this preset's section; switching off keeps its saved values." : "Enable this preset's section using Base as the starting point.", on, v => InstrumentEdit("preset override", () => { if (v) CopySection(p, fields); f.SetValue(p, v); }, true)), Button("Revert", "Copy this section's Base settings into the preset.", () => InstrumentEdit("revert preset section", () => CopySection(p, fields), true))));
        }
        void CopySection(ZTrackerInstrument.InstrumentPreset p, string[] fields)
        {
            foreach (string name in fields) { var dest = p.GetType().GetField(name); if (dest == null) continue; var value = instrument.GetType().GetField(name).GetValue(instrument); dest.SetValue(p, value is ZUIEnvelopeData env ? env.DeepCopy() : value); }
        }
        void Section(VisualElement root, string title, string flag, params string[] fields)
        {
            if (fields.Length == 1 && flag == null) { root.Add(FieldControl(instrument,fields[0])); return; }
            var card = Z.BoxKeyed(title, "Edit " + title.ToLowerInvariant() + " for this sound. Preset sections inherit Base unless Override is on.", "tracker.instrument." + title);
            OverrideHeader(card, flag, fields); object owner = SectionOwner(flag); var flow = Flow(); foreach (var name in fields) flow.Add(FieldControl(owner, name)); flow.SetEnabled(instrument.GetPreset(preset) == null || flag == null || owner != (object)instrument); card.Add(flow); root.Add(card);
        }
        void BuildPresets(VisualElement root)
        {
            if (instrument.presets == null) return;
            preset = Mathf.Clamp(preset, -1, instrument.presets.Count - 1);
            var box = Z.BoxKeyed("Presets", "Each preset overrides chosen sections; I00 uses Base and I01 uses the first preset.", "tracker.presets");
            box.Add(Z.MiniRadio(preset + 1, new[] { "Base" }.Concat(instrument.presets.Select(p => p.name)).ToArray(), "Choose the variation to author and audition. This view choice does not rewrite Base.", v => { StopPreview(); preset = v - 1; RebuildControls(); }, wrap: true));
            box.Add(Flow(Button("Add preset", "Add a variation inheriting every section from Base.", () => InstrumentEdit("add preset", () => { instrument.presets.Add(new ZTrackerInstrument.InstrumentPreset { name = "Preset " + (instrument.presets.Count + 1) }); preset = instrument.presets.Count - 1; }, true)), Button("Remove preset", "Remove selected preset; Undo restores it. Existing I commands retain their numeric positions.", () => { if (preset >= 0) InstrumentEdit("remove preset", () => { instrument.presets.RemoveAt(preset); preset = -1; }, true); })));
            if (preset >= 0) box.Add(Z.Field("Name", "Preset name.", Z.TextInput(instrument.presets[preset].name, "Rename this variation.", v => InstrumentEdit("preset name", () => instrument.presets[preset].name = v), 180))); root.Add(box);
        }
        void Parameter(VisualElement root, string title, string scalar, string envelope, string flag)
        {
            var card = Z.BoxKeyed(title, TipFor(scalar), "tracker.param." + scalar); OverrideHeader(card, flag, new[] { scalar, envelope }); object owner = SectionOwner(flag);
            var ef = owner.GetType().GetField(envelope); var env = (ZUIEnvelopeData)ef.GetValue(owner); bool on = env != null && env.enabled && env.Count > 0;
            card.Add(Flow(FieldControl(owner, scalar), Z.Toggle("Curve", on ? "Use the animated curve; turn off to use the scalar value." : "Animate this parameter over each voice's lifetime.", on, v => InstrumentEdit("parameter envelope", () => { if (env == null || env.Count == 0) { Vector2 bounds = Ranges[scalar]; env = new ZUIEnvelopeData(0, 1, bounds.x, bounds.y); foreach (var p in env.points) p.value = Convert.ToSingle(owner.GetType().GetField(scalar).GetValue(owner)); ef.SetValue(owner, env); } env.enabled = v; }, true))));
            if (on)
            {
                card.Add(Flow(Z.Field("Seconds", "Duration of the envelope timeline.", Z.Float(env.xMax, "Duration in seconds; preserves point times and endpoint.", v => InstrumentEdit("envelope duration", () => env.xMax = Mathf.Max(.001f, v), true), 90)),
                    Z.Segmented(env.loopEnabled ? env.loopMode : 0, new[] { "Once", "Loop", "Ping pong" }, "Replay the loop interval after reaching its end.", v => InstrumentEdit("envelope loop", () => { env.loopEnabled = v > 0; env.loopMode = v; }, true))));
                if (env.loopEnabled) card.Add(Z.MicroMinMax("Loop", env.loopStart, env.loopEnd, 0, env.xMax, "Time interval replayed by this envelope.", (lo, hi) => InstrumentEdit("envelope loop range", () => { env.loopStart = lo; env.loopEnd = hi; }), 270));
                card.Add(Named(Z.Envelope(env.points, new ZuiEnvelopeOptions { xMin = env.xMin, xMax = env.xMax, yMin = env.yMin, yMax = env.yMax, xAxisLabel = "Seconds", yAxisLabel = title, minPoints = 2 }, "Double click to add a point; drag points; Shift-right-drag bends a segment; Delete removes selected points.", EndInstrumentGesture, () => BeginInstrumentGesture("edit envelope"), 285, 120), "envelope-" + scalar));
            }
            if (instrument.GetPreset(preset) != null && owner == (object)instrument)
                foreach (var child in card.Children().Skip(1)) child.SetEnabled(false);
            root.Add(card);
        }
        void BuildADSR(VisualElement root)
        {
            object owner = SectionOwner("ovrAdsr"); float a = Convert.ToSingle(owner.GetType().GetField("attack").GetValue(owner)), d = Convert.ToSingle(owner.GetType().GetField("decay").GetValue(owner)), s = Convert.ToSingle(owner.GetType().GetField("sustain").GetValue(owner)), r = Convert.ToSingle(owner.GetType().GetField("release").GetValue(owner));
            var points = new List<ZUIEnvelopePoint> { new ZUIEnvelopePoint(0,0), new ZUIEnvelopePoint(a,1), new ZUIEnvelopePoint(a+d,s), new ZUIEnvelopePoint(a+d+r,0) };
            var canvas=Z.Envelope(points, new ZuiEnvelopeOptions { xMax = Mathf.Max(.01f,a+d+r), minPoints = 4, allowAddPoints = false, allowRemovePoints = false, allowExponentEdit = false, xAxisLabel = "Seconds" }, "Drag the peak, sustain or release point to shape the amplitude ADSR. The same values drive native playback.", () => { var type = owner.GetType(); type.GetField("attack").SetValue(owner, Mathf.Max(0,points[1].time)); type.GetField("decay").SetValue(owner, Mathf.Max(0,points[2].time-points[1].time)); type.GetField("sustain").SetValue(owner, Mathf.Clamp01(points[2].value)); type.GetField("release").SetValue(owner, Mathf.Max(0,points[3].time-points[2].time)); EndInstrumentGesture(); }, () => BeginInstrumentGesture("shape amplitude"), 285, 110);
            canvas.SetEnabled(instrument.GetPreset(preset)==null || owner!=(object)instrument); root.Add(canvas);
        }
        void InitializeFM()
        {
            if (instrument.fmOperators == null || instrument.fmOperators.Length != 4) instrument.fmOperators = Enumerable.Range(0,4).Select(_ => new ZTrackerInstrument.FMOperatorData { freqRatio = 1, level = 1, attack = .01f, decay = .2f, sustain = .7f, release = .5f }).ToArray();
        }
        void BuildFM(VisualElement root)
        {
            Section(root, "FM routing", null, "fmAlgorithm", "fmFeedback");
            if (instrument.fmOperators == null || instrument.fmOperators.Length != 4) { root.Add(Button("Initialize operators", "Create the four FM operators required by playback.", () => InstrumentEdit("initialize FM", InitializeFM, true))); return; }
            for (int i = 0; i < instrument.fmOperators.Length; i++) { int n = i; object op = instrument.fmOperators[i]; var card = Z.BoxKeyed("Operator " + (i + 1), "FM operator pitch, level and amplitude.", "tracker.fm." + i); var flow = Flow(); foreach (string f in new[] { "waveform", "freqRatio", "freqFixed", "level", "attack", "decay", "sustain", "release" }) flow.Add(FieldControl(op, f, v => instrument.fmOperators[n] = (ZTrackerInstrument.FMOperatorData)v)); card.Add(flow); root.Add(card); }
        }
        void BuildKit(VisualElement root)
        {
            var dropRoot = Z.Column(); root.Add(dropRoot); root = dropRoot;
            root.Add(FieldControl(instrument, "kitOverlap")); root.Add(Flow(Button("Add drum", "Add an empty drum entry at the next free MIDI note.", () => InstrumentEdit("add drum", () => { var list = (instrument.kitEntries ?? Array.Empty<ZTrackerInstrument.KitEntry>()).ToList(); int note = 36; while (list.Any(k => k.midiNote == note) && note < 126) note++; list.Add(new ZTrackerInstrument.KitEntry { displayName = "Drum", midiNote = note, baseNote = 60, volume = 1, sustain = 1, release = .1f }); instrument.kitEntries = list.ToArray(); }, true)), Named(Z.Object<AudioClip>(null, "Import one audio clip as a drum; drag files into the kit area to import several.", v => { if (v != null) AddKitClips(new[] { v }); }, 180), "import-drum")));
            root.RegisterCallback<DragUpdatedEvent>(e => { if (DragAndDrop.objectReferences.OfType<AudioClip>().Any()) DragAndDrop.visualMode = DragAndDropVisualMode.Copy; });
            root.RegisterCallback<DragPerformEvent>(e => { var clips = DragAndDrop.objectReferences.OfType<AudioClip>().ToArray(); if (clips.Length > 0) { DragAndDrop.AcceptDrag(); AddKitClips(clips); e.StopPropagation(); } });
            for (int i = 0; i < (instrument.kitEntries?.Length ?? 0); i++)
            {
                int n = i; object drum = instrument.kitEntries[i]; var card = Z.BoxKeyed(instrument.kitEntries[i].displayName, "Drag this drum's Move grip onto another to reorder. Its MIDI mapping stays attached to it.", "tracker.kit." + kitViewKeys[i]);
                var grip = Z.Text("Move", tooltip: "Drag this grip onto another drum's Move grip to reorder."); card.AddHeaderContent(grip);
                Reorder(grip, "kit", n, (a,z) => InstrumentEdit("reorder kit", () => { Undo.RecordObject(this,"Tracker: kit view order"); var list = instrument.kitEntries.ToList(); Move(list,a,z); Move(kitViewKeys,a,z); instrument.kitEntries = list.ToArray(); selectedKit = z; }, true));
                card.Add(Flow(Button("Preview", "Select and audition this mapped drum.", () => { selectedKit = n; Audition(instrument.kitEntries[n].midiNote); }), Button("Remove", "Remove this drum; Undo restores it.", () => InstrumentEdit("remove drum", () => { Undo.RecordObject(this,"Tracker: kit view removal"); var list = instrument.kitEntries.ToList(); list.RemoveAt(n); kitViewKeys.RemoveAt(n); instrument.kitEntries = list.ToArray(); }, true))));
                var flow = Flow(); foreach (string f in new[] { "displayName", "midiNote", "clip", "baseNote", "volume", "attack", "decay", "sustain", "release" }) flow.Add(FieldControl(drum, f, v => instrument.kitEntries[n] = (ZTrackerInstrument.KitEntry)v)); card.Add(flow); if (instrument.kitEntries[i].clip != null) card.Add(Waveform(instrument.kitEntries[i].clip, "Waveform of this drum sample.")); root.Add(card);
            }
        }
        void AddKitClips(AudioClip[] clips) { InstrumentEdit("import kit clips", () => { var list = (instrument.kitEntries ?? Array.Empty<ZTrackerInstrument.KitEntry>()).ToList(); foreach (var clip in clips) { int note = 36; while (list.Any(k => k.midiNote == note) && note < 126) note++; list.Add(new ZTrackerInstrument.KitEntry { clip = clip, displayName = clip.name, midiNote = note, baseNote = 60, volume = 1, sustain = 1, release = .1f }); } instrument.kitEntries = list.ToArray(); }, true); }
        void BuildArpeggio(VisualElement root)
        {
            var card = Z.BoxKeyed("Arpeggio", "Cycle semitone offsets from each note's base pitch.", "tracker.arpeggio"); card.Add(Flow(FieldControl(instrument,"arpeggioEnabled"), FieldControl(instrument,"arpeggioSpeed")));
            var flow = Flow(); for (int i = 0; i < (instrument.arpeggioNotes?.Length ?? 0); i++) { int n = i; flow.Add(Z.MicroSlider("Step " + (i+1), instrument.arpeggioNotes[i], -24, 24, "Semitone offset for this arpeggio step.", v => InstrumentEdit("arpeggio step", () => instrument.arpeggioNotes[n] = (int)v), 130, decimals:0)); }
            flow.Add(Button("Add step", "Append a unison step.", () => InstrumentEdit("add arpeggio step", () => instrument.arpeggioNotes = (instrument.arpeggioNotes ?? Array.Empty<int>()).Concat(new[] { 0 }).ToArray(), true)));
            flow.Add(Button("Remove step", "Remove the last arpeggio step.", () => { if (instrument.arpeggioNotes?.Length > 1) InstrumentEdit("remove arpeggio step", () => instrument.arpeggioNotes = instrument.arpeggioNotes.Take(instrument.arpeggioNotes.Length - 1).ToArray(), true); })); card.Add(flow); root.Add(card);
        }
        void BuildMacros(VisualElement root)
        {
            var box = Z.BoxKeyed("Macros", "Gxy sets macro x to y/15; Hxy slides it over one row. Blend, pulse width, B ratio and volume affect held voices; other mappings affect subsequent notes. Legacy macro evaluation runs on the editor/main thread.", "tracker.macros");
            box.Add(Button("Add macro", "Add one of up to four command-addressable macros.", () => { if ((instrument.macros?.Length ?? 0) < 4) InstrumentEdit("add macro", () => instrument.macros = (instrument.macros ?? Array.Empty<ZTrackerInstrument.MacroDef>()).Concat(new[] { new ZTrackerInstrument.MacroDef { name = "Macro " + (instrument.macros?.Length ?? 0), links = Array.Empty<ZTrackerInstrument.MacroLink>() } }).ToArray(), true); }));
            for (int m = 0; m < (instrument.macros?.Length ?? 0); m++)
            {
                int mi = m; var macro = instrument.macros[m]; var card = Z.BoxKeyed(m + " " + macro.name, "Macro identity, starting value and parameter mappings.", "tracker.macro." + macroViewKeys[m]);
                card.Add(Flow(Z.TextInput(macro.name, "Rename this macro.", v => InstrumentEdit("macro name", () => { var a = instrument.macros[mi]; a.name = v; instrument.macros[mi] = a; }), 120), Z.MicroSlider("Default", macro.defaultValue, 0,1,"Starting macro value before a G or H command.", v => InstrumentEdit("macro default", () => { var a = instrument.macros[mi]; a.defaultValue = v; instrument.macros[mi] = a; }),145), Button("Remove", "Remove this macro; later command indices shift.", () => InstrumentEdit("remove macro", () => { Undo.RecordObject(this,"Tracker: macro view removal"); var list = instrument.macros.ToList(); list.RemoveAt(mi); macroViewKeys.RemoveAt(mi); instrument.macros = list.ToArray(); },true))));
                card.Add(Button("Add link", "Map this macro to a parameter.", () => InstrumentEdit("add macro link", () => { var a = instrument.macros[mi]; a.links = (a.links ?? Array.Empty<ZTrackerInstrument.MacroLink>()).Concat(new[] { new ZTrackerInstrument.MacroLink { parameterName = "blend", maxValue = 1 } }).ToArray(); instrument.macros[mi] = a; },true)));
                for (int k = 0; k < (macro.links?.Length ?? 0); k++)
                {
                    int li = k; var link = macro.links[k]; bool reversed = link.minValue > link.maxValue;
                    Vector2 bounds = Ranges.TryGetValue(link.parameterName ?? "",out var b) ? b : new Vector2(0,1);
                    var target = Z.MiniRadio(Array.IndexOf(MacroParams,link.parameterName),MacroParams.Select(ObjectNames.NicifyVariableName).ToArray(),"Choose a parameter; existing unsupported legacy links are preserved until replaced.", v => InstrumentEdit("macro target", () => { var a = instrument.macros[mi]; a.links[li].parameterName = MacroParams[v]; instrument.macros[mi] = a; },true),wrap:true);
                    target.style.maxWidth = 285;
                    card.Add(target);
                    card.Add(Flow(Z.MicroMinMax("Range",Mathf.Min(link.minValue,link.maxValue),Mathf.Max(link.minValue,link.maxValue),bounds.x,bounds.y,"Parameter endpoints; Reverse makes the macro decrease this parameter.", (lo,hi) => InstrumentEdit("macro range", () => { var a = instrument.macros[mi]; a.links[li].minValue = reversed ? hi : lo; a.links[li].maxValue = reversed ? lo : hi; instrument.macros[mi] = a; }),270),
                        Z.Toggle("Reverse","Map macro zero to the upper endpoint and one to the lower endpoint.",reversed,v=>InstrumentEdit("reverse macro range",()=>{var a=instrument.macros[mi];float start=a.links[li].minValue;a.links[li].minValue=a.links[li].maxValue;a.links[li].maxValue=start;instrument.macros[mi]=a;},true)),
                        Button("Remove link","Remove this parameter mapping.", () => InstrumentEdit("remove macro link", () => { var a = instrument.macros[mi]; var list = a.links.ToList(); list.RemoveAt(li); a.links = list.ToArray(); instrument.macros[mi] = a; },true))));
                }
                box.Add(card);
            }
            root.Add(box);
        }
        void BuildPiano(VisualElement root)
        {
            var box = Z.BoxKeyed("Piano", "Click a pitch to audition; right click a key to assign the selected kit drum's note.", "tracker.piano"); var flow = Flow();
            for (int i = 0; i < 24; i++) { int pitch = Mathf.Clamp(octave*12+i,0,126); var key = Button(NoteName(pitch), "Audition MIDI note " + pitch + "; right click to map the last previewed kit drum here.", () => Audition(pitch), "piano-" + pitch); key.RegisterCallback<PointerDownEvent>(e => { if (e.button != 1 || instrument.type != InstrumentType.Kit || instrument.kitEntries == null || instrument.kitEntries.Length == 0) return; InstrumentEdit("map kit note", () => { int n = Mathf.Clamp(selectedKit,0,instrument.kitEntries.Length-1); instrument.kitEntries[n].midiNote = pitch; }, true); e.StopPropagation(); }); flow.Add(key); } box.Add(flow); root.Add(box);
        }
        VisualElement Waveform(AudioClip clip, string tip)
        {
            var canvas = new VisualElement { tooltip = tip + $" {clip.name}, {clip.frequency} Hz, {clip.length:0.00} seconds." }; canvas.style.width = 285; canvas.style.height = 70;
            canvas.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) Audition(instrument.type == InstrumentType.Kit && instrument.kitEntries?.Length > 0 ? instrument.kitEntries[Mathf.Clamp(selectedKit,0,instrument.kitEntries.Length-1)].midiNote : octave * 12); });
            var peaks = new float[285]; var samples = new float[Mathf.Min(clip.samples,4096)*clip.channels];
            for (int i = 0; i < peaks.Length; i++) { int offset = Mathf.Min(clip.samples-samples.Length/clip.channels, i*clip.samples/peaks.Length); if (clip.GetData(samples,Mathf.Max(0,offset))) { float peak = 0; foreach (float v in samples) peak = Mathf.Max(peak,Mathf.Abs(v)); peaks[i] = peak; } }
            canvas.generateVisualContent += ctx => { var p = ctx.painter2D; p.strokeColor = new Color(.35f,.75f,.85f); p.lineWidth = 1; for (int i=0;i<peaks.Length;i++) { float x = i*canvas.contentRect.width/peaks.Length; float h=peaks[i]*canvas.contentRect.height*.45f; p.BeginPath(); p.MoveTo(new Vector2(x,canvas.contentRect.height*.5f-h)); p.LineTo(new Vector2(x,canvas.contentRect.height*.5f+h)); p.Stroke(); } }; return canvas;
        }
    }
}
