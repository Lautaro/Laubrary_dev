using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Laubrary.Audio;
using Laubrary.Audio.Editor;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        [SerializeField] int instrumentTab, macroIndex, presetIndex = -1;
        [SerializeField] string sampleId = "", zoneId = "", modSetId = "", modDeviceId = "";
        [SerializeField] int instrumentChain, sampleSort;
        InstrumentData Instrument => ReadInstrument(instrument);
        int gestureGroup = -1;
        static InstrumentData NewInstrumentData()
        {
            var data = new InstrumentData { id = Id(), name = "Instrument", family = InstrumentFamily.Synth, parameters = new InstrumentParameters { type = InstrumentType.Synth, volume = .25f } };
            for (int i = 0; i < 8; i++) data.macros[i] = new InstrumentMacro { name = "Macro " + (i + 1) };
            data.parameters.fmOperators = Enumerable.Range(0, 4).Select(i => new ZTrackerInstrument.FMOperatorData { freqRatio = 1, level = i == 0 ? 1 : .5f, attack = .01f, decay = .2f, sustain = .7f, release = .5f }).ToArray();
            return data;
        }
        void BeginInstrumentGesture(string label)
        {
            if (gestureGroup >= 0) return;
            Undo.IncrementCurrentGroup(); gestureGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Tracker: " + label); CompleteUndo(instrument, "Tracker: " + label);
        }
        void EndInstrumentGesture()
        {
            EditorUtility.SetDirty(instrument); if (gestureGroup >= 0) CollapseUndo(gestureGroup,instrument); gestureGroup = -1;
            RefreshLive(instrument); RefreshTransport();
        }
        VisualElement InstrumentDial(string label, float value, float min, float max, string tip, Action<float> apply, string name = null, int decimals = 2)
            => Named(Dial(label, value, min, max, tip, v => InstrumentEdit(label, () => apply(v)), decimals), name);
        VisualElement InstrumentNumber(string label, float value, string tip, Action<float> apply, string name = null)
            => Z.Field(label, tip, Named(Z.Float(value, tip, v => InstrumentEdit(label, () => apply(v)), 90), name));
        VisualElement InstrumentToggle(string label, bool value, string tip, Action<bool> apply, string name = null, bool rebuild = false)
            => Named(Z.Toggle(label, tip, value, v => InstrumentEdit(label, () => apply(v), rebuild)), name);
        VisualElement InstrumentChoice(string label, int value, string[] labels, string tip, Action<int> apply, string name = null)
            => Z.Field(label, tip, Named(Z.MiniRadio(value, labels.Select(l=>l=="PingPong"?"Ping-pong":l=="NoteOff"?"Note off":l).ToArray(), tip, v => InstrumentEdit(label, () => apply(v), true), wrap: true), name));
        static VisualElement Diagnostic(string text, string tip) => Z.Text("⚠ " + text, tooltip: tip);
        SampleData SelectedSample => Instrument?.sampler.samples.Find(s => s.id == sampleId);
        Keyzone SelectedZone => Instrument?.sampler.zones.Find(z => z.id == zoneId);
        partial void BuildInstrument(VisualElement root)
        {
            if (instrument == null && Data.instruments.Count > 0) instrument = Data.instruments[Mathf.Clamp(entryInstrument, 0, Data.instruments.Count - 1)];
            var d = Instrument; if (d == null) return;
            var header = Flow(Named(Z.TextInput(d.name, "Instrument display name.", v => { InstrumentEdit("instrument name", () => d.name = v); RefreshInstrumentNames(); }, 175), "instrument-name"),
                InstrumentChoice("Family", (int)d.family, new[] { "Sampler", "Synth" }, "Both families retain their settings. Changing source topology applies after Stop/Play.", v => { d.family = (InstrumentFamily)v; d.parameters.type = v == 0 ? InstrumentType.Sample : d.synthMode == SynthMode.FM ? InstrumentType.FM : InstrumentType.Synth; instrumentTab = 0; }, "instrument-family")); root.Add(header);
            var tabs = d.family == InstrumentFamily.Sampler ? new[] { "Samples", "Zones", "Modulation", "Effects", "Macros", "Presets" } : new[] { "Synth", "Envelopes", "Modulation", "Effects", "Macros", "Presets" };
            instrumentTab = Mathf.Clamp(instrumentTab, 0, tabs.Length - 1);
            root.Add(Named(Z.Segmented(instrumentTab, tabs, "Choose the instrument settings to edit.", v => { instrumentTab = v; BuildPane(); }), "instrument-tabs"));
            var scroll = AuthoringScroll(root, "instrument-" + instrumentTab);
            if (instrumentTab == 0) { if (d.family == InstrumentFamily.Sampler) { BuildSamplePicker(controls); BuildSampler(scroll); } else BuildSynth(scroll); }
            else if (instrumentTab == 1) { if (d.family == InstrumentFamily.Sampler) { BuildSamplePicker(controls); BuildZones(scroll); } else BuildToneEnvelopes(scroll); }
            else if (instrumentTab == 2) BuildModulation(scroll);
            else if (instrumentTab == 3) BuildInstrumentChains(scroll);
            else if (instrumentTab == 4) BuildMacros(scroll);
            else BuildPresets(scroll);
            if (d.diagnostics.Count > 0) header.Add(Diagnostic("Imported", string.Join("\n", d.diagnostics)));
        }
        void AddSample()
        {
            InstrumentEdit("add sample", () => { var s = new SampleData { id = Id(), name = "Sample " + (Instrument.sampler.samples.Count + 1), nna = Instrument.sampler.nna }; Instrument.sampler.samples.Add(s); sampleId = s.id; }, true);
        }
        void AssignSample(SampleData s, AudioClip clip)
        {
            InstrumentEdit("assign PCM", () => { s.pcm = clip; if (clip != null) { if (s.name.StartsWith("Sample ", StringComparison.Ordinal)) s.name = clip.name; s.regionStartFrame = 0; s.regionEndFrame = clip.samples; s.loopStartFrame = 0; s.loopEndFrame = clip.samples; s.sliceMarkers.Clear(); } }, true);
        }
        void ReorderSamples(int from, int to)
        {
            var list = Instrument.sampler.samples; var before = list.Select(s => s.id).ToArray(); Move(list, from, to);
            foreach (var zone in Instrument.sampler.zones) if (zone.sample >= 0 && zone.sample < before.Length) zone.sample = list.FindIndex(s => s.id == before[zone.sample]);
        }
        void RemoveSample(SampleData s)
        {
            InstrumentEdit("remove sample", () => { int at = Instrument.sampler.samples.IndexOf(s); Instrument.sampler.zones.RemoveAll(z => z.sample == at); foreach (var z in Instrument.sampler.zones) if (z.sample > at) z.sample--; foreach (var other in Instrument.sampler.samples) if (other.parentSampleId == s.id) other.parentSampleId = ""; Instrument.sampler.samples.Remove(s); sampleId = Instrument.sampler.samples.FirstOrDefault()?.id ?? ""; }, true);
        }
        void BuildSamplePicker(VisualElement root)
        {
            var samples = Instrument.sampler.samples;
            if (SelectedSample == null) sampleId = samples.FirstOrDefault()?.id ?? "";
            var box = Z.BoxKeyed("Samples", "Select to edit; preview plays the assigned PCM; drag the grip to reorder and retain zone references.", "tracker.instrument.samples");
            box.Add(Flow(Button("Add sample", "Create a sample record. Assign an AudioClip in its details.", AddSample, "add-sample"), Z.Segmented(sampleSort, new[] { "Order", "Name" }, "Sort the picker display; authored playback order changes only by dragging.", v => { sampleSort = v; BuildPane(); })));
            var cards = new VisualElement();cards.style.minWidth=0; foreach (var s in sampleSort == 0 ? samples : samples.OrderBy(s => s.name).ToList())
            {
                int at = samples.IndexOf(s); var card = Flow(); card.style.flexWrap = Wrap.NoWrap;card.style.width=Length.Percent(100);card.style.minWidth=0;
                var grip = Button("⋮", "Drag to reorder samples; zone references follow their sample.", () => { }, "sample-grip-" + s.id); Reorder(grip, "instrument-samples", at, (a, b) => InstrumentEdit("reorder samples", () => ReorderSamples(a, b), true)); card.Add(grip);
                var choose = Button(at.ToString("00") + " " + s.name, "Select this sample's settings: "+s.name, () => { sampleId = s.id; BuildPane(); }, "sample-select-" + s.id);choose.style.flexGrow=1;choose.style.flexShrink=1;choose.style.minWidth=0;choose.style.width=0;choose.style.overflow=Overflow.Hidden;choose.style.textOverflow=TextOverflow.Ellipsis;choose.EnableInClassList("tracker-picked", s.id == sampleId); card.Add(choose);
                card.Add(Button("▶", "Preview this sample's AudioClip.", () => PreviewClip(s.pcm), "sample-preview-" + s.id)); cards.Add(card);
            } box.Add(cards); root.Add(box);
        }
        static void PreviewClip(AudioClip clip)
        {
            if (clip == null) return;
            var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil"); var play = util?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null); play?.Invoke(null, new object[] { clip, 0, false });
        }
        void BuildSampler(VisualElement root)
        {
            var sampler = Instrument.sampler;
            root.Add(Flow(InstrumentDial("Gain", sampler.volume, 0, 16, "Sampler global linear gain, applied once.", v => sampler.volume = v, "sampler-volume"), InstrumentDial("Pan", sampler.pan, -1, 1, "Sampler global pan.", v => sampler.pan = v), InstrumentDial("Transpose", sampler.transpose, -120, 120, "Sampler transpose in semitones.", v => sampler.transpose = (int)v, decimals: 0), InstrumentDial("Fine", sampler.fineTuneCents, -1200, 1200, "Sampler fine tuning in cents.", v => sampler.fineTuneCents = v)));
            root.Add(InstrumentChoice("New note action", (int)sampler.nna, new[] { "Cut", "Note off", "Continue" }, "Default copied to newly added samples. Each sample's own new note action is authoritative.", v => sampler.nna = (NewNoteAction)v, "sampler-default-nna"));
            var s = SelectedSample; if (s == null) return;
            root.Add(Flow(Z.Field("Name", "Sample record name.", Z.TextInput(s.name, "Rename this sample.", v => InstrumentEdit("sample name", () => s.name = v), 160)), Z.Field("Sample audio", "The original AudioClip remains unchanged by edits.", Named(Z.Object(s.pcm, "Assign a readable AudioClip.", v => AssignSample(s, v), 190), "sample-pcm")), Button("▶", "Preview the assigned sample audio.", () => PreviewClip(s.pcm), "sample-preview"), Button("Remove sample", "Remove this sample and its zones; Undo restores them.", () => RemoveSample(s), "remove-sample")));
            BuildWaveform(root, s);
            root.Add(Flow(InstrumentDial("Gain", s.volume, 0, 16, "Sample linear gain.", v => s.volume = v, "sample-volume"), InstrumentDial("Pan", s.pan, -1, 1, "Sample pan.", v => s.pan = v), InstrumentDial("Transpose", s.transpose, -120, 120, "Sample transpose in semitones.", v => s.transpose = (int)v, decimals: 0), InstrumentDial("Fine", s.fineTuneCents, -1200, 1200, "Sample tuning in cents.", v => s.fineTuneCents = v), InstrumentDial("Base note", s.baseNote, 0, 119, "Default base note copied to new zones; zone base note controls playback.", v => s.baseNote = (int)v, decimals: 0)));
            root.Add(InstrumentChoice("Loop", (int)s.loop, new[] { "Off", "Forward", "Backward", "Ping-pong" }, "Loop start is inclusive; end is exclusive. Drag either highlighted waveform boundary.", v => { s.loop = (SampleLoop)v; if (v != 0 && s.pcm != null && s.loopEndFrame - s.loopStartFrame < 2) { s.loopStartFrame = s.regionStartFrame; s.loopEndFrame = s.regionEndFrame > 0 ? s.regionEndFrame : s.pcm.samples; } }, "sample-loop-mode"));
            root.Add(Flow(InstrumentChoice("Interpolation", (int)s.interpolation, new[] { "Linear", "Cubic" }, "Sample interpolation method.", v => s.interpolation = (SampleInterpolation)v), InstrumentToggle("Exit on release", s.releaseExitsLoop, "Continue beyond the loop after note release.", v => s.releaseExitsLoop = v), InstrumentToggle("One shot", s.oneShot, "Ignore note-off and play until the sample ends.", v => s.oneShot = v), InstrumentToggle("Inactive", s.inactive, "Preserve this sample but exclude it from preparation.", v => s.inactive = v)));
            root.Add(Flow(InstrumentChoice("New note action", (int)s.nna, new[] { "Cut", "Note off", "Continue" }, "Action on previous voices when a new note starts on their column.", v => s.nna = (NewNoteAction)v, "sample-nna"), Z.Field("Mute group", "Matching nonnegative groups choke each other; -1 is unassigned.", Named(Z.Int(s.muteGroup, "Mute group or -1.", v => InstrumentEdit("mute group", () => s.muteGroup = Math.Max(-1, v)), 85), "sample-mute-group"))));
            root.Add(IndexPicker("Modulation", s.modulationSet, Instrument.modulation.Select(m => m.name).ToArray(), v => s.modulationSet = v, "sample-modulation"));
            root.Add(IndexPicker("FX chain", s.fxChain, Instrument.fxChains.Select((c, i) => "Chain " + (i + 1)).ToArray(), v => s.fxChain = v, "sample-fx-chain"));
            if (s.pcm != null)
            {
                int end = s.regionEndFrame > 0 ? s.regionEndFrame : s.pcm.samples;
                root.Add(Named(Z.MicroMinMax("Region", s.regionStartFrame, end, 0, s.pcm.samples, "Playable region, start inclusive and end exclusive.", (a, b) => InstrumentEdit("sample region", () => { s.regionStartFrame = Mathf.Clamp((int)a, 0, Math.Max(0, s.pcm.samples - 2)); s.regionEndFrame = Mathf.Clamp((int)b, Math.Min(s.pcm.samples, s.regionStartFrame + 2), s.pcm.samples); s.loopStartFrame = Mathf.Clamp(s.loopStartFrame, s.regionStartFrame, s.regionEndFrame - 2); s.loopEndFrame = Mathf.Clamp(s.loopEndFrame, s.loopStartFrame + 2, s.regionEndFrame); s.sliceMarkers.RemoveAll(m => m < s.regionStartFrame || m >= s.regionEndFrame); }, true), 250), "sample-region"));
                root.Add(Flow(Button("Add slice", "Insert a slice marker halfway through the largest free region.", () => InstrumentEdit("add slice", () => { var points = new[] { s.regionStartFrame }.Concat(s.sliceMarkers).Concat(new[] { end }).Distinct().OrderBy(n => n).ToArray(); var gap = Enumerable.Range(0, points.Length - 1).OrderByDescending(i => points[i + 1] - points[i]).First(); int frame = (points[gap] + points[gap + 1]) / 2; if (!s.sliceMarkers.Contains(frame)) s.sliceMarkers.Add(frame); s.sliceMarkers.Sort(); }, true), "add-slice"), Button("Clear slices", "Remove all slice markers; Undo restores them.", () => InstrumentEdit("clear slices", () => s.sliceMarkers.Clear(), true), "clear-slices")));
                foreach (int frame in s.sliceMarkers.ToArray()) { int at = s.sliceMarkers.IndexOf(frame); root.Add(Flow(InstrumentDial("Slice " + (at + 1), frame, s.regionStartFrame, end - 1, "Slice start frame; markers are unique and sorted.", v => { int n = (int)v; if (!s.sliceMarkers.Contains(n) || n == frame) { s.sliceMarkers[at] = n; s.sliceMarkers.Sort(); } }, decimals: 0), Button("×", "Remove this slice.", () => InstrumentEdit("remove slice", () => s.sliceMarkers.Remove(frame), true), "remove-slice-" + at))); }
            }
            var parents = Instrument.sampler.samples.Where(p => p != s && p.parentSampleId == "").ToList(); var labels = new[] { "None" }.Concat(parents.Select(p => p.name)).ToArray(); int parent = parents.FindIndex(p => p.id == s.parentSampleId) + 1;
            root.Add(InstrumentChoice("Slice parent", parent, labels, "Borrow the parent's slice markers. This sample's PCM and region remain authored independently.", v => s.parentSampleId = v == 0 ? "" : parents[v - 1].id));
        }
        VisualElement IndexPicker(string title, int index, string[] choices, Action<int> apply, string name)
            => InstrumentChoice(title, index + 1, new[] { "None" }.Concat(choices).ToArray(), "Pick a declared " + title.ToLowerInvariant() + "; None leaves it unassigned.", v => apply(v - 1), name);
        void BuildWaveform(VisualElement root, SampleData sample)
        {
            var canvas = new VisualElement { name = "sample-waveform", tooltip = "Drag cyan loop boundaries; Shift-click sets start and Alt-click sets end. Vertical lines mark slices. AudioClip PCM remains immutable." };
            canvas.style.height = 130; canvas.style.flexShrink = 0; canvas.style.backgroundColor = new Color(.08f, .1f, .13f); root.Add(canvas);
            var clip = sample.pcm; float[] peaks = new float[512];
            if (clip != null) { try { var pcm = new float[clip.samples * clip.channels]; if (clip.GetData(pcm, 0)) for (int i = 0; i < pcm.Length; i++) peaks[Math.Min(511, (int)((long)i * 512 / pcm.Length))] = Mathf.Max(peaks[Math.Min(511, (int)((long)i * 512 / pcm.Length))], Mathf.Abs(pcm[i])); } catch (Exception e) { canvas.tooltip += "\nPCM preview unavailable: " + e.Message; } }
            float X(int frame) => clip == null ? 0 : frame / (float)Math.Max(1, clip.samples) * canvas.contentRect.width;
            int Frame(float x) => clip == null ? 0 : Mathf.RoundToInt(Mathf.Clamp01(x / Math.Max(1, canvas.contentRect.width)) * clip.samples);
            canvas.generateVisualContent += ctx => { var p = ctx.painter2D; float h = canvas.contentRect.height; p.strokeColor = new Color(.5f, .65f, .75f); p.lineWidth = 1; for (int i = 0; i < 512; i++) { float x = i / 511f * canvas.contentRect.width; p.BeginPath(); p.MoveTo(new Vector2(x, h * (.5f - peaks[i] * .45f))); p.LineTo(new Vector2(x, h * (.5f + peaks[i] * .45f))); p.Stroke(); } foreach (var frame in sample.sliceMarkers.Concat(new[] { sample.loopStartFrame, sample.loopEndFrame })) { p.strokeColor = frame == sample.loopStartFrame || frame == sample.loopEndFrame ? Color.cyan : Color.yellow; p.lineWidth = 2; p.BeginPath(); p.MoveTo(new Vector2(X(frame), 0)); p.LineTo(new Vector2(X(frame), h)); p.Stroke(); } };
            int edge = -1; void Set(float x) { int end = sample.regionEndFrame > 0 ? sample.regionEndFrame : clip.samples; int at = Frame(x); if (edge == 0) sample.loopStartFrame = Mathf.Clamp(at, sample.regionStartFrame, Math.Max(sample.regionStartFrame, sample.loopEndFrame - 2)); else sample.loopEndFrame = Mathf.Clamp(at, sample.loopStartFrame + 2, end); canvas.MarkDirtyRepaint(); }
            canvas.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0 || clip == null || clip.samples < 2) return; float x = e.localPosition.x; edge = e.shiftKey ? 0 : e.altKey ? 1 : Mathf.Abs(x - X(sample.loopStartFrame)) < Mathf.Abs(x - X(sample.loopEndFrame)) ? 0 : 1; BeginInstrumentGesture("waveform loop"); canvas.CapturePointer(e.pointerId); Set(x); });
            canvas.RegisterCallback<PointerMoveEvent>(e => { if (edge >= 0 && canvas.HasPointerCapture(e.pointerId)) Set(e.localPosition.x); });
            canvas.RegisterCallback<PointerUpEvent>(e => { if (edge < 0) return; edge = -1; canvas.ReleasePointer(e.pointerId); EndInstrumentGesture(); BuildPane(); });
            if (clip != null && clip.samples >= 2) root.Add(Named(Z.MicroMinMax("Loop frames", sample.loopStartFrame, sample.loopEndFrame, sample.regionStartFrame, sample.regionEndFrame > 0 ? sample.regionEndFrame : clip.samples, "Loop bounds in source frames, end exclusive; minimum span two frames.", (a, b) => InstrumentEdit("loop frames", () => { int end = sample.regionEndFrame > 0 ? sample.regionEndFrame : clip.samples; sample.loopStartFrame = Mathf.Clamp((int)a, sample.regionStartFrame, Math.Max(sample.regionStartFrame, end - 2)); sample.loopEndFrame = Mathf.Clamp((int)b, sample.loopStartFrame + 2, end); canvas.MarkDirtyRepaint(); }), 250), "sample-loop-frames"));
        }
        void AddZone()
        {
            var s = SelectedSample; if (s == null) return;
            InstrumentEdit("add keyzone", () => { var z = new Keyzone { id = Id(), sample = Instrument.sampler.samples.IndexOf(s), baseNote = s.baseNote }; Instrument.sampler.zones.Add(z); zoneId = z.id; }, true);
        }
        void BuildZones(VisualElement root)
        {
            var d = Instrument; var zones = d.sampler.zones; if (SelectedZone == null) zoneId = zones.FirstOrDefault()?.id ?? "";
            root.Add(Flow(Button("Add zone", "Create a full note/velocity zone for the selected sample.", AddZone, "add-zone"), Button("Clone zone", "Duplicate the selected zone with a new identity.", () => { if (SelectedZone == null) return; InstrumentEdit("clone zone", () => { var z = Clone(SelectedZone); z.id = Id(); zones.Add(z); zoneId = z.id; }, true); }, "clone-zone"), Button("Remove zone", "Remove the selected zone; Undo restores it.", () => { if (SelectedZone != null) InstrumentEdit("remove zone", () => zones.Remove(SelectedZone), true); }, "remove-zone")));
            var canvas = new VisualElement { name = "keyzone-map", tooltip = "Horizontal: C-0 to B-9; vertical: velocity 0–127. Drag a zone's interior to move it, its edges to resize. Overlapping zones layer together." }; canvas.style.height = Mathf.Clamp(position.height-250,140,330); canvas.style.flexShrink = 0; canvas.style.backgroundColor = new Color(.08f, .1f, .13f); root.Add(canvas);
            canvas.schedule.Execute(()=>{float h=Mathf.Clamp(position.height-250,140,330);if(Mathf.Abs(canvas.resolvedStyle.height-h)>.5f)canvas.style.height=h;}).Every(150);
            Rect RectOf(Keyzone z){var a=PlotArea(canvas);return new Rect(a.x+z.noteMin/120f*a.width,a.y+(127-z.velocityMax)/128f*a.height,(z.noteMax-z.noteMin+1)/120f*a.width,(z.velocityMax-z.velocityMin+1)/128f*a.height);}
            void Labels(){canvas.Clear();var a=PlotArea(canvas);for(int n=0;n<120;n+=12)PlotLabel(canvas,"C-"+(n/12),a.x+n/120f*a.width,a.yMax+2,30);PlotLabel(canvas,"B-9",a.xMax-24,a.yMax+2,24);foreach(int v in new[]{0,32,64,96,127})PlotLabel(canvas,v.ToString(),1,a.y+(127-v)/127f*a.height-8,30);PlotLabel(canvas,"Vel",1,0,30).style.height=16;foreach(var z in zones){var r=RectOf(z);var label=PlotLabel(canvas,z.sample>=0&&z.sample<d.sampler.samples.Count?d.sampler.samples[z.sample].name:"Missing sample",r.x+3,r.y+3,Math.Max(1,r.width-6));label.AddToClassList("tracker-zone-label");label.style.height=Math.Min(22,r.height);label.name="zone-label-"+z.id;}}
            canvas.RegisterCallback<GeometryChangedEvent>(_=>Labels());
            canvas.generateVisualContent += ctx => { var p = ctx.painter2D;var a=PlotArea(canvas); p.lineWidth = 1; for (int n = 0; n <= 120; n += 12) { p.strokeColor = Color.gray; p.BeginPath(); p.MoveTo(new Vector2(a.x+n/120f*a.width,a.y)); p.LineTo(new Vector2(a.x+n/120f*a.width,a.yMax)); p.Stroke(); }foreach(int v in new[]{0,32,64,96,127}){float y=a.y+(127-v)/127f*a.height;p.BeginPath();p.MoveTo(new Vector2(a.x,y));p.LineTo(new Vector2(a.xMax,y));p.Stroke();} foreach (var z in zones) { var r = RectOf(z); p.fillColor = z.inactive ? new Color(.3f, .3f, .3f, .4f) : new Color(.2f, .55f, .8f, .45f); p.strokeColor = z.id == zoneId ? Color.yellow : Color.cyan; p.lineWidth = z.id == zoneId ? 3 : 1; p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); p.Fill(); p.Stroke();if(z.id==zoneId)foreach(var h in new[]{r.min,r.max,new Vector2(r.xMax,r.yMin),new Vector2(r.xMin,r.yMax)}){p.fillColor=Color.yellow;p.BeginPath();p.Arc(h,3,0,360);p.Fill();} } };
            Keyzone dragged = null, original = null; Vector2 origin = default; int edges = 0;
            canvas.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; var hit = zones.Where(z => RectOf(z).Contains(e.localPosition)).OrderByDescending(z => z.id == zoneId).FirstOrDefault(); if (hit == null) return; dragged = hit; original = Clone(hit); zoneId = hit.id; origin = e.localPosition; var r = RectOf(hit); edges = (Mathf.Abs(origin.x - r.xMin) < 7 ? 1 : 0) | (Mathf.Abs(origin.x - r.xMax) < 7 ? 2 : 0) | (Mathf.Abs(origin.y - r.yMax) < 7 ? 4 : 0) | (Mathf.Abs(origin.y - r.yMin) < 7 ? 8 : 0); BeginInstrumentGesture("keyzone range"); canvas.CapturePointer(e.pointerId); canvas.MarkDirtyRepaint(); });
            canvas.RegisterCallback<PointerMoveEvent>(e => { if (dragged == null || !canvas.HasPointerCapture(e.pointerId)) return;var a=PlotArea(canvas); int dn = Mathf.RoundToInt((e.localPosition.x - origin.x) / a.width * 120); int dv = Mathf.RoundToInt((origin.y - e.localPosition.y) / a.height * 128); SetZoneRange(dragged, original, dn, dv, edges);Labels(); canvas.MarkDirtyRepaint(); });
            canvas.RegisterCallback<PointerUpEvent>(e => { if (dragged == null) return; dragged = null; canvas.ReleasePointer(e.pointerId); EndInstrumentGesture(); BuildPane(); });
            if (zones.Count == 0) return; root.Add(Z.MiniRadio(Math.Max(0, zones.FindIndex(z => z.id == zoneId)), zones.Select((z, i) => (i + 1) + " " + (z.sample >= 0 && z.sample < d.sampler.samples.Count ? d.sampler.samples[z.sample].name : "Missing")).ToArray(), "Select a keyzone by its sample.", v => { zoneId = zones[v].id; BuildPane(); }, wrap: true));
            var zone = SelectedZone; if (zone == null) return;
            root.Add(InstrumentChoice("Sample", zone.sample, d.sampler.samples.Select(s => s.name).ToArray(), "Pick the sample played by this zone.", v => zone.sample = v, "zone-sample"));
            root.Add(Flow(Named(Z.MicroMinMax("Notes", zone.noteMin, zone.noteMax, 0, 119, "Inclusive MIDI note range.", (a, b) => InstrumentEdit("zone notes", () => { zone.noteMin = (int)a; zone.noteMax = (int)b;Labels(); canvas.MarkDirtyRepaint(); }), 190), "zone-notes"), Named(Z.MicroMinMax("Velocity", zone.velocityMin, zone.velocityMax, 0, 127, "Inclusive MIDI velocity range.", (a, b) => InstrumentEdit("zone velocity", () => { zone.velocityMin = (int)a; zone.velocityMax = (int)b;Labels(); canvas.MarkDirtyRepaint(); }), 190), "zone-velocity"), InstrumentDial("Base note", zone.baseNote, 0, 119, "Note played at source frequency.", v => zone.baseNote = (int)v, "zone-base-note", 0), InstrumentToggle("Key tracking", zone.keyTracking, "Follow played note pitch; off is fixed-pitch drum playback.", v => zone.keyTracking = v, "zone-keytracking"), InstrumentToggle("Active", !zone.inactive, zone.inactive ? "Enable this zone so matching notes play its sample." : "Disable this zone while keeping its sample and ranges.", v => zone.inactive = !v, "zone-active", true)));
            BuildBlend(root, zone);
        }
        static Rect PlotArea(VisualElement canvas)=>new Rect(36,24,Math.Max(1,canvas.contentRect.width-44),Math.Max(1,canvas.contentRect.height-48));
        static Label PlotLabel(VisualElement canvas,string text,float x,float y,float width){var l=Z.Text(text,tooltip:text);l.pickingMode=PickingMode.Ignore;l.AddToClassList("tracker-axis-label");Place(l,x,y,width);canvas.Add(l);return l;}
        static void SetZoneRange(Keyzone z, Keyzone start, int noteDelta, int velocityDelta, int edges)
        {
            if (edges == 0) { int dn = Mathf.Clamp(noteDelta, -start.noteMin, 119 - start.noteMax), dv = Mathf.Clamp(velocityDelta, -start.velocityMin, 127 - start.velocityMax); z.noteMin = start.noteMin + dn; z.noteMax = start.noteMax + dn; z.velocityMin = start.velocityMin + dv; z.velocityMax = start.velocityMax + dv; return; }
            if ((edges & 1) != 0) z.noteMin = Mathf.Clamp(start.noteMin + noteDelta, 0, z.noteMax); if ((edges & 2) != 0) z.noteMax = Mathf.Clamp(start.noteMax + noteDelta, z.noteMin, 119); if ((edges & 4) != 0) z.velocityMin = Mathf.Clamp(start.velocityMin + velocityDelta, 0, z.velocityMax); if ((edges & 8) != 0) z.velocityMax = Mathf.Clamp(start.velocityMax + velocityDelta, z.velocityMin, 127);
        }
        void BuildBlend(VisualElement root, Keyzone zone)
        {
            var paired = InstrumentToggle("Second sample", zone.blend != null, zone.blend == null ? "Add a second sample with independent tuning and loops." : "Remove this zone's second sample and its blend settings; Undo restores them.", v => zone.blend = v ? new SampleBlendExtension() : null, "zone-paired", true);
            var b = zone.blend; if (b == null) { root.Add(paired); return; }
            var box = Z.BoxKeyed("Second sample", "Blend a second sample into this zone, with independent tuning and loops. This extension needs an interchange warning for Renoise.", "tracker.zone.blend");
            box.AddHeaderContent(paired);
            box.Add(Flow(Z.Field("Second audio", "The second sample's audio source.", Named(Z.Object(b.pcmB, "Assign the second sample's AudioClip.", v => InstrumentEdit("second sample audio", () => { b.pcmB = v; b.loopStartFrameB = 0; b.loopEndFrameB = v != null ? v.samples : 0; }, true), 185), "zone-pcm-b")), Button("▶", "Preview the second sample.", () => PreviewClip(b.pcmB), "zone-preview-b"), InstrumentDial("Base note (second)", b.baseNoteB, 0, 119, "Second sample note played at source frequency.", v => b.baseNoteB = (int)v, decimals: 0), InstrumentDial("Fine tune (second)", b.fineTuneBCents, -1200, 1200, "Second sample tuning in cents.", v => b.fineTuneBCents = v)));
            box.Add(InstrumentChoice("Blend", b.mode, new[] { "Mix", "Ring", "Sync", "PM" }, "How the second sample combines with the first.", v => b.mode = v)); box.Add(Flow(InstrumentDial("Amount", b.amount, 0, 1, "Second sample blend amount.", v => b.amount = v), InstrumentNumber("PM frames", b.pmDepth, "Phase modulation depth in source frames.", v => b.pmDepth = Math.Max(0, v)), InstrumentToggle("ADSR", b.envelopeEnabled, "Apply an amplitude envelope to the second sample blend.", v => b.envelopeEnabled = v)));
            BuildNumericFields(box, b, new[] { "attack", "decay", "sustain", "release" }, "paired");
            box.Add(InstrumentChoice("Second loop", (int)b.loopB, new[] { "Off", "Forward", "Backward", "PingPong" }, "Independent loop mode for the second sample.", v => b.loopB = (SampleLoop)v)); if (b.pcmB != null) box.Add(Z.MicroMinMax("Second loop frames", b.loopStartFrameB, b.loopEndFrameB, 0, b.pcmB.samples, "Second sample loop bounds, exclusive end; minimum two frames.", (a, e) => InstrumentEdit("second sample loop", () => { b.loopStartFrameB = Mathf.Clamp((int)a, 0, Math.Max(0, b.pcmB.samples - 2)); b.loopEndFrameB = Mathf.Clamp((int)e, Math.Min(b.pcmB.samples, b.loopStartFrameB + 2), b.pcmB.samples); }), 230)); box.Add(InstrumentToggle("Exit on release", b.releaseExitsLoopB, "Leave the second sample's loop on note release.", v => b.releaseExitsLoopB = v));
            BuildRetainedEnvelope(box, "Blend curve", b.blendEnvelope, v => b.blendEnvelope = v, 0, 1, "paired-blend-curve"); BuildRetainedEnvelope(box, "PM curve", b.pmEnvelope, v => b.pmEnvelope = v, 0, 65536, "paired-pm-curve"); root.Add(box);
        }
        static string[] WaveLabels(SoundEnumDomain domain) => domain == SoundEnumDomain.SavedAuthoring ? new[] { "Sine", "Triangle", "Saw", "Square", "Noise" } : new[] { "Sine", "Square", "Saw", "Reverse saw", "Triangle", "White noise", "Pink noise" };
        void BuildSynth(VisualElement root)
        {
            var d = Instrument; var q = d.parameters;
            root.Add(InstrumentChoice("New note action", (int)d.sampler.nna, new[] { "Cut", "Note off", "Continue" }, "Synth action on previous voices when another note starts on the same column.", v => d.sampler.nna = (NewNoteAction)v, "synth-nna"));
            root.Add(InstrumentChoice("Engine", (int)d.synthMode, new[] { "Subtractive", "FM" }, "Switch synth topology. Existing inactive settings are retained.", v => { d.synthMode = (SynthMode)v; q.type = v == 0 ? InstrumentType.Synth : InstrumentType.FM; }, "synth-mode"));
            root.Add(InstrumentChoice("Wave domain", (int)q.enumDomain, new[] { "Saved", "Native direct" }, "Saved integer mapping: Sine 0→0, Triangle 1→4, Saw 2→2, Square 3→1, Noise 4→5. Native integers 0–6 are direct. This changes interpretation without rewriting stored wave integers.", v => q.enumDomain = (SoundEnumDomain)v, "synth-wave-domain"));
            if (d.synthMode == SynthMode.Subtractive)
            {
                root.Add(Flow(InstrumentChoice("Wave A", q.waveA, WaveLabels(q.enumDomain), "Primary oscillator shape in the selected integer domain.", v => q.waveA = v, "synth-wave-a"), InstrumentChoice("Wave B", q.waveB, WaveLabels(q.enumDomain), "Secondary oscillator shape in the selected integer domain.", v => q.waveB = v, "synth-wave-b")));
                root.Add(InstrumentChoice("Blend", q.blendMode, new[] { "Mix", "Ring", "Sync", "PM" }, "Blend the primary and secondary oscillators.", v => q.blendMode = v, "synth-blend-mode"));
                BuildNumericFields(root, q, new[] { "blend", "waveBRatio", "pmDepth", "pulseWidth", "unisonVoices", "unisonDetune", "unisonSpread" }, "synth");
                root.Add(InstrumentToggle("Blend ADSR", q.blendEnvelope, "Apply the secondary oscillator blend ADSR.", v => q.blendEnvelope = v)); BuildNumericFields(root, q, new[] { "blendAttack", "blendDecay", "blendSustain", "blendRelease" }, "synth");
            }
            else
            {
                root.Add(InstrumentChoice("Algorithm", q.fmAlgorithm < 6 ? q.fmAlgorithm : -1, new[] { "1 Stack", "2 Split", "3 Branch", "4 Pairs", "5 Fan", "6 Parallel" }, "Implemented algorithms 0–5 only. Saved 6 or 7 is retained and renders the documented algorithm-5 fallback.", v => q.fmAlgorithm = v, "fm-algorithm"));
                if (q.fmAlgorithm >= 6) root.Add(Diagnostic("Algorithm " + q.fmAlgorithm, "Preserved imported integer. The engine uses algorithm 5 fallback; choose an implemented algorithm to replace it explicitly."));
                BuildNumericFields(root, q, new[] { "fmFeedback" }, "synth");
                if (q.fmOperators == null || q.fmOperators.Length != 4) root.Add(Button("Initialize operators", "Create four sine operators, retaining all other settings. Existing malformed operator payload is retained in the legacy archive if available.", () => InstrumentEdit("initialize FM", () => q.fmOperators = NewInstrumentData().parameters.fmOperators, true), "initialize-fm"));
                else for (int i = 0; i < 4; i++)
                {
                    int at = i; var op = q.fmOperators[i]; var box = Z.BoxKeyed("Operator " + (i + 1), "FM operators render sine only. Ratio pitch is ignored when fixed frequency is positive.", "tracker.fm." + i);
                    if (op.waveform != 0) box.Add(Flow(Diagnostic("Wave " + op.waveform, "Imported non-sine waveform is preserved but ignored by the FM engine."), Button("Use sine", "Explicitly replace the preserved unsupported operator wave with sine.", () => InstrumentEdit("FM sine", () => { var value = q.fmOperators[at]; value.waveform = 0; q.fmOperators[at] = value; }, true), "fm-sine-" + i)));
                    else box.Add(Z.Text("Sine", tooltip: "The FM engine implements sine operators only."));
                    foreach (var field in new[] { "freqRatio", "freqFixed", "level", "attack", "decay", "sustain", "release" })
                    {
                        var f = typeof(ZTrackerInstrument.FMOperatorData).GetField(field); string id = "fmOperators." + at + "." + field; float value = (float)f.GetValue(op);
                        box.Add(NumberForField(field, value, v => { object next = q.fmOperators[at]; f.SetValue(next, v); q.fmOperators[at] = (ZTrackerInstrument.FMOperatorData)next; }, id));
                    } root.Add(box);
                }
            }
            BuildNumericFields(root, q, new[] { "volume", "pan", "fineTune", "attack", "decay", "sustain", "release", "vibratoDepth", "vibratoRate", "vibratoFadeIn", "vibratoRandomness" }, "synth");
            root.Add(Flow(InstrumentToggle("Glide", q.glideEnabled, "Glide from previous pitch.", v => q.glideEnabled = v), InstrumentToggle("Legato", q.glideLegato, "Apply glide only across legato notes.", v => q.glideLegato = v))); BuildNumericFields(root, q, new[] { "glideSeconds" }, "synth");
            root.Add(Flow(InstrumentToggle("Arpeggio", q.arpeggioEnabled, "Cycle through authored semitone offsets.", v => q.arpeggioEnabled = v), InstrumentToggle("Per note", q.arpeggioSpeedIsPerNote, "Speed is each note's duration; off divides whole-sequence duration among the notes.", v => q.arpeggioSpeedIsPerNote = v))); BuildNumericFields(root, q, new[] { "arpeggioSpeed" }, "synth");
            root.Add(Button("Add arp note", "Append a semitone offset to the arpeggio.", () => InstrumentEdit("add arp note", () => q.arpeggioNotes = (q.arpeggioNotes ?? Array.Empty<int>()).Concat(new[] { 0 }).ToArray(), true), "add-arp-note"));
            var notes = Flow(); for (int i = 0; i < (q.arpeggioNotes?.Length ?? 0); i++) { int at = i; var grip = Button("⋮", "Drag to reorder arpeggio notes.", () => { }); Reorder(grip, "arp-notes", i, (a, b) => InstrumentEdit("reorder arp notes", () => { var list = q.arpeggioNotes.ToList(); Move(list, a, b); q.arpeggioNotes = list.ToArray(); }, true)); var card = Flow(grip, InstrumentDial("Note " + (i + 1), q.arpeggioNotes[i], -120, 120, "Arpeggio offset in semitones.", v => q.arpeggioNotes[at] = (int)v, decimals: 0), Button("×", "Remove this arpeggio note.", () => InstrumentEdit("remove arp note", () => q.arpeggioNotes = q.arpeggioNotes.Where((n, j) => j != at).ToArray(), true), "remove-arp-note-" + i)); notes.Add(card); } root.Add(notes);
            BuildPoints(root, q.arpeggioSpeedPoints, "Arp speed", 0, 3600, .0001f, 3600, "arp-speed", false);
            root.Add(InstrumentToggle("Filter", q.instFilterEnabled, "Enable the per-voice filter.", v => q.instFilterEnabled = v)); root.Add(InstrumentChoice("Mode", q.instFilterMode, new[] { "Low pass", "High pass", "Band pass" }, "Per-voice filter mode.", v => q.instFilterMode = v)); BuildNumericFields(root, q, new[] { "instFilterCutoff", "instFilterResonance", "instDelaySend", "instReverbSend" }, "synth");
            if (q.instDelaySend > 0 || q.instReverbSend > 0) root.Add(Diagnostic("Send routing", "Synth delay and reverb sends route to the first explicit Send track containing the matching Delay or Reverb effect. Declare those buses in Mixer before playback."));
        }
        static string FieldLabel(string name)
        {
            switch (name) { case "freqRatio": return "Ratio"; case "freqFixed": return "Fixed Hz"; case "fineTune": return "Fine cents"; case "instFilterCutoff": return "Cutoff"; case "instFilterResonance": return "Resonance"; case "instDelaySend": return "Delay send"; case "instReverbSend": return "Reverb send"; }
            return ObjectNames.NicifyVariableName(name);
        }
        VisualElement NumberForField(string field, float value, Action<float> apply, string name)
        {
            string label = FieldLabel(field), tip = label + " authored setting."; float lo = 0, hi = 1; bool bounded = true; int decimals = 3;
            if (field == "volume" || field == "level") { hi = 16; tip = "Linear amplitude gain."; }
            else if (field == "pan") { lo = -1; tip = "Stereo position from left -1 through centre 0 to right 1."; }
            else if (field == "pulseWidth") { lo = .01f; hi = .99f; tip = "Square-wave duty cycle."; }
            else if (field == "unisonVoices") { lo = 1; hi = 8; decimals = 0; tip = "Number of detuned oscillator voices."; }
            else if (field == "fineTune" || field == "fineTuneB" || field == "fineTuneBCents") { lo = -1200; hi = 1200; tip = "Pitch tuning in cents."; }
            else if (field == "baseNote" || field == "baseNoteB") { hi = 119; decimals = 0; tip = "MIDI base note played at source frequency."; }
            else if (field.Contains("Resonance")) { lo = .1f; hi = 10; tip = "Filter resonance Q."; }
            else if (field == "instFilterCutoff") { tip = "Retained filter cutoff normalized against the output sample rate Nyquist frequency."; }
            else if (field == "vibratoDepth" || field == "unisonDetune") { hi = 1200; tip = "Pitch depth in cents."; }
            else if (field == "vibratoRate" || field == "rate") { hi = 1000; tip = "Cycles per second (Hz)."; }
            else if (field == "fmFeedback") { hi = 16; tip = "FM phase feedback."; }
            else if (field == "waveBRatio" || field == "freqRatio") { hi = 64; tip = "Frequency ratio."; }
            else if (field == "freqFixed") { hi = 96000; tip = "Fixed frequency in Hz; zero uses ratio pitch."; }
            else if (field == "pmDepth") { hi = 65536; tip = "Phase modulation depth: cycles for synth, frames for paired PCM."; }
            else if (field == "attack" || field == "hold" || field == "decay" || field == "release" || field.EndsWith("Attack") || field.EndsWith("Decay") || field.EndsWith("Release") || field == "glideSeconds" || field == "vibratoFadeIn" || field == "arpeggioSpeed" || field == "duration") { bounded = false; tip = "Duration in seconds, nonnegative and at most 3600; arpeggio durations must be positive."; }
            else if (field == "depth" || field == "min" || field == "max" || field == "curve") { bounded = false; tip = field == "curve" ? "Positive exponent." : "Physical output value in the selected modulation target's units."; }
            if (bounded) return InstrumentDial(label, value, lo, hi, tip, apply, name, decimals);
            return InstrumentNumber(label, value, tip, v => apply(field == "curve" ? Math.Max(.0001f, v) : field == "min" || field == "max" || field == "depth" ? v : Mathf.Clamp(v, field == "arpeggioSpeed" ? .0001f : 0, 3600)), name);
        }
        void BuildNumericFields(VisualElement root, object owner, string[] fields, string prefix)
        {
            var row = Flow(); foreach (string name in fields) { var f = owner.GetType().GetField(name); if (f == null) continue; bool integer = f.FieldType == typeof(int); float value = Convert.ToSingle(f.GetValue(owner)); row.Add(NumberForField(name, value, v => f.SetValue(owner, integer ? (object)(int)v : v), prefix + "-" + name)); } root.Add(row);
        }
        void BuildToneEnvelopes(VisualElement root)
        {
            var q = Instrument.parameters;
            if (Instrument.synthMode == SynthMode.FM) { root.Add(Diagnostic("Retained curves", "Oscillator parameter envelopes are preserved while FM is selected. FM uses its operators' amplitude envelopes; switch to Subtractive to author these curves.")); return; }
            root.Add(InstrumentChoice("Loop domain", (int)q.envelopeEnumDomain, new[] { "Saved", "Native direct" }, "Saved loops: 1 forward, 2 ping-pong. Native direct: 0 forward, 1 ping-pong. Stored inactive integers are retained.", v => q.envelopeEnumDomain = (SoundEnumDomain)v, "envelope-domain"));
            BuildRetainedEnvelope(root, "Blend", q.blendEnvelopeData, v => q.blendEnvelopeData = v, 0, 1, "tone-blend"); BuildRetainedEnvelope(root, "Pulse width", q.pulseWidthEnvelopeData, v => q.pulseWidthEnvelopeData = v, .01f, .99f, "tone-pulse-width"); BuildRetainedEnvelope(root, "B ratio", q.waveBRatioEnvelopeData, v => q.waveBRatioEnvelopeData = v, 0, 64, "tone-b-ratio"); BuildRetainedEnvelope(root, "PM depth", q.pmDepthEnvelopeData, v => q.pmDepthEnvelopeData = v, 0, 65536, "tone-pm-depth"); BuildRetainedEnvelope(root, "Detune", q.unisonDetuneEnvelopeData, v => q.unisonDetuneEnvelopeData = v, 0, 12000, "tone-detune");
        }
        void BuildRetainedEnvelope(VisualElement root, string title, ZUIEnvelopeData env, Action<ZUIEnvelopeData> assign, float min, float max, string name)
        {
            var box = Z.BoxKeyed(title, "Parameter envelope evaluated on the audio thread. Double-click adds points; drag moves; Delete removes selection; Shift-right-drag shapes a segment.", "tracker.envelope." + name);
            box.Add(Flow(Button(env == null ? "Create curve" : "Remove curve", env == null ? "Create an enabled envelope with two points." : "Remove the envelope; Undo restores it.", () => InstrumentEdit("envelope", () => assign(env == null ? new ZUIEnvelopeData(0, 1, min, max) : null), true), name + "-create"), env != null ? InstrumentToggle("On", env.enabled, "Enable this retained envelope.", v => env.enabled = v, name + "-enabled") : null));
            if (env != null)
            {
                box.Add(Flow(InstrumentNumber("Duration", env.xMax, "Envelope duration in seconds; moves its required end point.", v => env.xMax = Mathf.Clamp(v, .0001f, 3600), name + "-duration"), InstrumentToggle("Loop", env.loopEnabled, "Loop the selected time range.", v => { env.loopEnabled = v; if (v && env.loopMode == 0 && Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring) env.loopMode = 1; }, name + "-loop"), InstrumentChoice("Mode", env.loopMode - (Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring ? 1 : 0), new[] { "Forward", "PingPong" }, "Loop integer follows the explicit envelope domain.", v => env.loopMode = v + (Instrument.parameters.envelopeEnumDomain == SoundEnumDomain.SavedAuthoring ? 1 : 0))));
                box.Add(Z.MicroMinMax("Loop time", env.loopStart, env.loopEnd, 0, Math.Max(.0001f, env.xMax), "Loop start/end in seconds.", (a, b) => InstrumentEdit("envelope loop", () => { env.loopStart = a; env.loopEnd = Math.Max(a + .0001f, b); }), 220));
                var canvas = Z.Envelope(env.points, new ZuiEnvelopeOptions { xMin = env.xMin, xMax = Math.Max(.0001f, env.xMax), yMin = min, yMax = max, xAxisLabel = "Seconds", yAxisLabel = title, minPoints = 2 }, "Double-click adds points; drag moves; Delete removes; Shift-right-drag bends.", EndInstrumentGesture, () => BeginInstrumentGesture("edit " + title + " envelope"), 380, 140); canvas.name = name + "-canvas"; canvas.style.maxWidth = Length.Percent(100); box.Add(canvas);
            } root.Add(box);
        }
        void BuildPoints(VisualElement root, List<ModulationPoint> points, string title, float xmin, float xmax, float ymin, float ymax, string name, bool exponent = true)
        {
            var box = Z.BoxKeyed(title, "Ordered unique time/value points. Drag on the canvas or use the exact fields.", "tracker.points." + name);
            box.Add(Button("Add point", "Append a point after the last one.", () => InstrumentEdit("add " + title + " point", () => { double t = points.Count == 0 ? xmin : points.Last().time + (xmax <= 1 ? .1 : 1); if (t <= xmax) points.Add(new ModulationPoint { time = t, value = Mathf.Clamp(points.LastOrDefault()?.value ?? .5f, ymin, ymax) }); }, true), name + "-add-point"));
            var detached = points.Select(p => new ZUIEnvelopePoint((float)p.time, p.value, p.exponent)).ToList(); float visibleMax = xmax <= 1 ? xmax : Math.Max(1, (float)points.Select(p => p.time).DefaultIfEmpty(1).Max());
            var canvas = Z.Envelope(detached, new ZuiEnvelopeOptions { xMin = xmin, xMax = visibleMax, yMin = ymin, yMax = ymax, minPoints = 0, allowExponentEdit = exponent, xAxisLabel = xmax <= 1 ? "Input" : "Seconds", yAxisLabel = title }, "Double-click to add; drag points; Delete removes. Explicit mapping curves use linear segments.", () => { points.Clear(); points.AddRange(detached.OrderBy(p => p.time).GroupBy(p => p.time).Select(g => g.First()).Select(p => new ModulationPoint { time = p.time, value = p.value, exponent = exponent ? p.exponent : 1 })); EndInstrumentGesture(); }, () => BeginInstrumentGesture("edit " + title + " curve"), 380, 135); canvas.name = name + "-canvas"; canvas.style.maxWidth = Length.Percent(100); box.Add(canvas);
            for (int i = 0; i < points.Count; i++) { var point = points[i]; int at = i; box.Add(Flow(InstrumentNumber("Time", (float)point.time, "Unique point time in " + (xmax <= 1 ? "normalized input" : "seconds") + ".", v => { double t = Mathf.Clamp(v, xmin, xmax); if (!points.Any(p => p != point && p.time == t)) { point.time = t; points.Sort((a, b) => a.time.CompareTo(b.time)); } }, name + "-time-" + i), InstrumentNumber("Value", point.value, "Point value, range " + ymin + " to " + ymax + ".", v => point.value = Mathf.Clamp(v, ymin, ymax), name + "-value-" + i), exponent ? InstrumentNumber("Exponent", point.exponent, "Positive segment exponent.", v => point.exponent = Math.Max(.0001f, v)) : null, Button("×", "Remove this point.", () => InstrumentEdit("remove point", () => points.Remove(point), true), name + "-remove-point-" + at))); } root.Add(box);
        }
        void BuildModulation(VisualElement root)
        {
            var d = Instrument; var sets = d.modulation;
            root.Add(Button("Add mod set", "Declare a sampler modulation set; assign it from each sample's Modulation picker. Synth uses its declared sets together.", () => InstrumentEdit("add modulation set", () => { var set = new ModulationSet { id = Id(), name = "Modulation " + (sets.Count + 1) }; sets.Add(set); modSetId = set.id; }, true), "add-mod-set"));
            var setData = sets.Find(s => s.id == modSetId) ?? sets.FirstOrDefault(); if (setData == null) return; modSetId = setData.id;
            var picker = Flow(); foreach (var set in sets) { int at = sets.IndexOf(set); var b = Button(set.name, "Select this set; drag to reorder with sample assignments preserved.", () => { modSetId = set.id; modDeviceId = ""; BuildPane(); }, "mod-set-" + set.id); Reorder(b, "mod-sets", at, (a, z) => InstrumentEdit("reorder modulation sets", () => { var previous = sets.ToArray(); Move(sets, a, z); foreach (var s in d.sampler.samples) if (s.modulationSet >= 0 && s.modulationSet < previous.Length) s.modulationSet = sets.IndexOf(previous[s.modulationSet]); }, true)); picker.Add(b); } controls.Add(picker);
            root.Add(Flow(Z.TextInput(setData.name, "Set display name.", v => InstrumentEdit("modulation name", () => setData.name = v), 180), Button("Remove set", "Remove this set and clear affected sample assignments.", () => InstrumentEdit("remove modulation set", () => { int at = sets.IndexOf(setData); sets.Remove(setData); foreach (var s in d.sampler.samples) { if (s.modulationSet == at) s.modulationSet = -1; else if (s.modulationSet > at) s.modulationSet--; } }, true), "remove-mod-set"), InstrumentChoice("Filter", setData.filterType, new[] { "Off", "Low pass", "High pass", "Band pass" }, "Sampler set filter type. Synth filter uses its Synth settings instead.", v => setData.filterType = v, "mod-filter")));
            Button add = null; add = (Button)Button("Add device…", "Choose an implemented modulation device.", () => { var menu = Z.Menu(add); foreach (var kind in new[] { ModulationDeviceKind.AHDSR, ModulationDeviceKind.Multipoint, ModulationDeviceKind.LFO, ModulationDeviceKind.Velocity, ModulationDeviceKind.KeyTracking, ModulationDeviceKind.Fader }) { var k = kind; menu.Item(ObjectNames.NicifyVariableName(k.ToString()), "Create this modulation device.", () => AddModDevice(setData, k)); } menu.Show(); }, "add-mod-device"); root.Add(add);
            foreach (var m in setData.devices.ToArray())
            {
                int at = setData.devices.IndexOf(m); var b = Button((at + 1) + " " + ObjectNames.NicifyVariableName(m.kind.ToString()) + " / " + ObjectNames.NicifyVariableName(m.target.ToString()), "Select device details; drag to change evaluation order.", () => { modDeviceId = m.id; BuildPane(); }, "mod-device-" + m.id); Reorder(b, "mod-devices", at, (a, z) => InstrumentEdit("reorder modulation devices", () => Move(setData.devices, a, z), true)); controls.Add(b);
            }
            var device = setData.devices.Find(m => m.id == modDeviceId) ?? setData.devices.FirstOrDefault(); if (device == null) return; modDeviceId = device.id;
            if (device.kind == ModulationDeviceKind.Stepper) { root.Add(Flow(Diagnostic("Preserved Stepper", "Stepper has no engine evaluator. Raw source retained: " + device.rawSource), Button("Remove device", "Explicitly remove this preserved unsupported device; Undo restores its raw payload.", () => InstrumentEdit("remove preserved mod device", () => setData.devices.Remove(device), true), "remove-mod-device"))); return; }
            root.Add(Flow(InstrumentToggle("On", device.enabled, "Enable device evaluation.", v => device.enabled = v, "mod-enabled"), InstrumentChoice("Target", (int)device.target, Enum.GetNames(typeof(ModulationTarget)), "Volume linear gain; pan normalized; pitch semitones; cutoff Hz; resonance Q; drive physical amount.", v => { device.target = (ModulationTarget)v; device.units = v == 2 ? "semitones" : v == 3 ? "Hz" : "normalized"; }, "mod-target"), InstrumentChoice("Operation", (int)device.operation, new[] { "Add", "Multiply", "Replace" }, "Apply each device in list order.", v => device.operation = (ModulationOperation)v, "mod-operation"), Button("Remove device", "Remove the selected device; Undo restores it.", () => InstrumentEdit("remove mod device", () => setData.devices.Remove(device), true), "remove-mod-device")));
            root.Add(InstrumentChoice("Units", Array.IndexOf(new[] { "seconds", "normalized", "semitones", "Hz" }, device.units), new[] { "seconds", "normalized", "semitones", "Hz" }, "Declared modulation units; point times remain seconds. Output units follow the selected target.", v => device.units = new[] { "seconds", "normalized", "semitones", "Hz" }[v], "mod-units"));
            if (device.kind == ModulationDeviceKind.AHDSR) BuildNumericFields(root, device, new[] { "attack", "hold", "decay", "sustain", "release" }, "mod");
            if (device.kind == ModulationDeviceKind.LFO) { root.Add(InstrumentChoice("Shape", device.lfoShape, new[] { "Sine", "Triangle", "Saw", "Square" }, "LFO waveform.", v => device.lfoShape = v)); BuildNumericFields(root, device, new[] { "rate", "depth", "phase" }, "mod"); }
            if (device.kind == ModulationDeviceKind.Velocity || device.kind == ModulationDeviceKind.KeyTracking || device.kind == ModulationDeviceKind.Fader) { BuildNumericFields(root, device, new[] { "min", "max", "curve" }, "mod"); if (device.kind == ModulationDeviceKind.Fader) BuildNumericFields(root, device, new[] { "duration", "depth" }, "mod"); }
            if (device.kind == ModulationDeviceKind.Multipoint)
            {
                float end = Math.Max(1, (float)device.points.Select(p => p.time).DefaultIfEmpty(1).Max());
                root.Add(Flow(InstrumentToggle("Sustain", device.sustainEnabled, "Hold at the sustain position until release.", v => device.sustainEnabled = v), InstrumentNumber("Sustain time", (float)device.sustainPosition, "Sustain time in seconds.", v => device.sustainPosition = Mathf.Clamp(v, 0, end)), InstrumentToggle("Loop", device.loopEnabled, "Loop points while held; release proceeds forward.", v => { device.loopEnabled = v; if (v && device.loop == SampleLoop.Off) device.loop = SampleLoop.Forward; }), InstrumentChoice("Loop mode", Math.Max(0, (int)device.loop - 1), new[] { "Forward", "Backward", "PingPong" }, "Held-note curve loop direction.", v => device.loop = (SampleLoop)(v + 1))));
                root.Add(Z.MicroMinMax("Loop seconds", (float)device.loopStart, (float)device.loopEnd, 0, end, "Loop time range in seconds.", (a, b) => InstrumentEdit("modulation loop", () => { device.loopStart = a; device.loopEnd = Math.Max(a + .0001f, b); }), 230));
                float min = device.target == ModulationTarget.Pan ? -1 : device.target == ModulationTarget.Pitch ? -120 : 0, max = device.target == ModulationTarget.Pitch ? 120 : device.target == ModulationTarget.Cutoff ? 96000 : device.target == ModulationTarget.Drive ? 100 : device.target == ModulationTarget.Volume ? 16 : device.target == ModulationTarget.Resonance ? 10 : 1;
                BuildPoints(root, device.points, "Points", 0, 3600, min, max, "mod-points");
            }
        }
        void AddModDevice(ModulationSet set, ModulationDeviceKind kind)
            => InstrumentEdit("add modulation device", () => { var m = new ModulationDevice { id = Id(), kind = kind }; if (kind == ModulationDeviceKind.Multipoint) m.points.AddRange(new[] { new ModulationPoint { time = 0, value = 1 }, new ModulationPoint { time = 1, value = 1 } }); set.devices.Add(m); modDeviceId = m.id; }, true);
        void BuildInstrumentChains(VisualElement root)
        {
            var chains = Instrument.fxChains;
            root.Add(Flow(Button("Add chain", "Declare an AudioCore instrument chain; assign it from each sampler sample. Synth renders Chain 1; drag another chain to the first position to use it.", () => InstrumentEdit("add instrument chain", () => { chains.Add(new AudioEffectChainData()); instrumentChain = chains.Count - 1; }, true), "add-instrument-chain"), Button("Remove chain", "Remove selected chain and clear affected sample assignments.", () => { if (chains.Count == 0) return; InstrumentEdit("remove instrument chain", () => { chains.RemoveAt(instrumentChain); foreach (var s in Instrument.sampler.samples) { if (s.fxChain == instrumentChain) s.fxChain = -1; else if (s.fxChain > instrumentChain) s.fxChain--; } instrumentChain = Math.Max(0, instrumentChain - 1); }, true); }, "remove-instrument-chain")));
            if (chains.Count == 0) return; instrumentChain = Mathf.Clamp(instrumentChain, 0, chains.Count - 1);
            var picks = Flow(); for (int i = 0; i < chains.Count; i++) { int at = i; var pick = Button("Chain " + (i + 1), "Select this chain; drag to reorder with sampler assignments preserved.", () => { instrumentChain = at; BuildPane(); }, "instrument-chain-" + i); Reorder(pick, "instrument-chains", at, (a, b) => InstrumentEdit("reorder instrument chains", () => { var previous = chains.ToArray(); var selected = chains[instrumentChain]; Move(chains, a, b); foreach (var s in Instrument.sampler.samples) if (s.fxChain >= 0 && s.fxChain < previous.Length) s.fxChain = chains.IndexOf(previous[s.fxChain]); instrumentChain = chains.IndexOf(selected); }, true)); picks.Add(pick); } root.Add(picks);
            root.Add(Named(new AudioChainEditor(() => Instrument.fxChains[instrumentChain], (label, action, topology) => InstrumentEdit(label, action),directAdd:true), "instrument-chain-editor"));
        }
        sealed class InstrumentDestination { public string label; public ParameterTarget target; }
        List<InstrumentDestination> InstrumentDestinations(bool external)
        {
            var choices = new List<InstrumentDestination>();
            foreach (var asset in external ? new[] { instrument } : Data.instruments.Where(a => a != null && a.schemaVersion == 1).Distinct())
            {
                var d = asset.model; bool sampler = d.family == InstrumentFamily.Sampler, fm = !sampler && d.synthMode == SynthMode.FM;
                foreach (TrackerParameter p in Enum.GetValues(typeof(TrackerParameter)))
                {
                    if (p == TrackerParameter.Count) continue;
                    if ((p >= TrackerParameter.Op0Ratio || p == TrackerParameter.FMFeedback) && !fm) continue;
                    if ((p == TrackerParameter.UnisonDetune || p == TrackerParameter.UnisonSpread || p == TrackerParameter.PulseWidth || p == TrackerParameter.WaveBRatio) && (sampler || fm)) continue;
                    if ((p == TrackerParameter.Blend || p == TrackerParameter.PMDepth) && (fm || sampler && !d.sampler.zones.Any(z => z.blend?.pcmB != null))) continue;
                    string key = ParameterName(p); string units = p == TrackerParameter.PMDepth ? sampler ? "frames" : "cycles" : TrackerParameters.Units(p);
                    string label=p>=TrackerParameter.Op0Ratio?"Operator "+(((int)p-(int)TrackerParameter.Op0Ratio)/7+1)+" "+FieldLabel(key.Substring(key.LastIndexOf('.')+1)):FieldLabel(key);
                    choices.Add(new InstrumentDestination { label = d.name + " / " + label + " (" + units + ")", target = new ParameterTarget { kind = sampler ? ParameterKind.Sample : ParameterKind.Synth, instrumentId = d.id, parameter = key, units = units, index = -1 } });
                }
                if (external) for (int m = 0; m < 8; m++) choices.Add(new InstrumentDestination { label = d.name + " / Macro " + (m + 1), target = new ParameterTarget { kind = ParameterKind.InstrumentMacro, instrumentId = d.id, parameter = "macro", units = "normalized", index = m } });
            } return choices;
        }
        static string ParameterName(TrackerParameter p)
        {
            if (p >= TrackerParameter.Op0Ratio) { int n = (int)p - (int)TrackerParameter.Op0Ratio; return "fmOperators." + n / 7 + "." + new[] { "freqRatio", "freqFixed", "level", "attack", "decay", "sustain", "release" }[n % 7]; }
            switch (p) { case TrackerParameter.FilterCutoff: return "instFilterCutoff"; case TrackerParameter.FilterResonance: return "instFilterResonance"; case TrackerParameter.FineTune: return "fineTune"; case TrackerParameter.FMFeedback: return "fmFeedback"; case TrackerParameter.PMDepth: return "pmDepth"; }
            string s = p.ToString(); return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
        static bool SameTarget(ParameterTarget a, ParameterTarget b) => a.kind == b.kind && a.instrumentId == b.instrumentId && a.parameter == b.parameter && a.index == b.index && a.units == b.units && !a.unresolved;
        Mapping DefaultMapping(bool external)
        {
            var target = InstrumentDestinations(external).FirstOrDefault(t => t.target.parameter == "volume");
            return new Mapping { target = target != null ? Clone(target.target) : new ParameterTarget { unresolved = true }, min = 0, max = 1 };
        }
        void BuildMacros(VisualElement root)
        {
            var d = Instrument; macroIndex = Mathf.Clamp(macroIndex, 0, 7);
            controls.Add(Named(Z.MiniRadio(macroIndex, Enumerable.Range(0, 8).Select(i => (i + 1) + " " + (d.macros[i]?.name ?? "Missing")).ToArray(), "Select one of the eight audio-thread macros.", v => { macroIndex = v; BuildPane(); }, wrap: true), "instrument-macros"));
            var macro = d.macros[macroIndex]; if (macro == null) { root.Add(Button("Initialize macro", "Create the missing macro record explicitly.", () => InstrumentEdit("initialize macro", () => d.macros[macroIndex] = new InstrumentMacro { name = "Macro " + (macroIndex + 1) }, true), "initialize-macro")); return; }
            int index = macroIndex;
            root.Add(Flow(Named(Z.TextInput(macro.name, "Macro name declared once.", v => InstrumentEdit("macro name", () => macro.name = v), 180), "macro-name"), Named(Dial("Value", macro.value, 0, 1, "Authored normalized macro default. Live preserving refresh also keeps Undo/Redo audible on held notes.", v => InstrumentMacroEdit("macro value", index, v, () => macro.value = v), 3), "macro-value"), Button("Add mapping", "Add a typed physical parameter mapping.", () => InstrumentEdit("add macro mapping", () => macro.mappings.Add(DefaultMapping(false)), true), "add-macro-mapping")));
            foreach (var mapping in macro.mappings.ToArray()) { int at = macro.mappings.IndexOf(mapping); var box = Z.BoxKeyed("Mapping " + (at + 1), "Mappings evaluate in list order. Drag the grip to reorder.", "tracker.macro.mapping." + index + "." + at); var grip = Button("⋮", "Drag to reorder mappings without changing target identity.", () => { }, "macro-mapping-grip-" + at); Reorder(grip, "macro-mappings", at, (a, b) => InstrumentEdit("reorder macro mappings", () => Move(macro.mappings, a, b), true)); box.Add(Flow(grip, Button("Remove mapping", "Remove this route; Undo restores it.", () => InstrumentEdit("remove macro mapping", () => macro.mappings.Remove(mapping), true), "remove-macro-mapping-" + at))); BuildMapping(box, mapping, false, "macro-mapping-" + at); root.Add(box); }
            if (d.family == InstrumentFamily.Synth) BuildExternalMap(root);
        }
        void BuildMapping(VisualElement root, Mapping mapping, bool external, string name)
        {
            var targets = InstrumentDestinations(external); var selected = targets.Find(t => SameTarget(t.target, mapping.target)); Button choose = null;
            choose = (Button)Button(selected?.label ?? "⚠ Retained target", "Choose a typed target, instrument owner and physical units. Retained unresolved targets are not silently replaced.", () => { var menu = Z.Menu(choose).Search("Find parameter…").Width(330); foreach (var target in targets) { var pick = target; menu.Item(pick.label, "Use this physical parameter target.", () => InstrumentEdit("mapping target", () => { mapping.target = Clone(pick.target); }, true), @checked: pick == selected); } menu.Show(); }, name + "-target"); choose.style.width = 330; choose.style.maxWidth = Length.Percent(100); root.Add(choose);
            root.Add(Flow(InstrumentNumber("At zero", mapping.min, "Physical target value when normalized input is zero. Equal or reversed endpoints are valid. Units: " + mapping.target.units, v => mapping.min = v, name + "-min"), InstrumentNumber("At one", mapping.max, "Physical target value when normalized input is one. Units: " + mapping.target.units, v => mapping.max = v, name + "-max"), InstrumentNumber("Quantum", mapping.quantum, "Output quantization step in target units; zero is continuous.", v => mapping.quantum = Math.Max(0, v), name + "-quantum"), InstrumentNumber("Lower", mapping.lower, "Quantization origin in target units.", v => mapping.lower = v, name + "-lower")));
            root.Add(Flow(InstrumentChoice("Scaling", mapping.scaling == "Linear" ? 0 : -1, new[] { "Linear" }, "Implemented scaling is Linear. Imported unsupported transforms are preserved and diagnosed; choose Linear to replace explicitly.", v => mapping.scaling = "Linear", name + "-scaling"), InstrumentToggle("Curve", mapping.curvePoints.Count > 0, "Use an explicit normalized piecewise-linear input transform. Empty means Linear.", v => { mapping.curvePoints.Clear(); if (v) mapping.curvePoints.AddRange(new[] { new ModulationPoint { time = 0, value = 0 }, new ModulationPoint { time = 1, value = 1 } }); mapping.curve = 1; }, name + "-curve", true)));
            if (mapping.curve != 1 || mapping.scaling != "Linear" || selected == null) root.Add(Flow(Diagnostic("Retained route", "Unresolved target or unsupported transform remains authored. Legacy link: " + mapping.legacyLink + "; scaling: " + mapping.scaling + "; curve: " + mapping.curve), Button("Linear curve", "Explicitly replace the retained scalar curve with the implemented linear curve.", () => InstrumentEdit("linear route curve", () => mapping.curve = 1, true), name + "-linear")));
            if (mapping.curvePoints.Count > 0) BuildPoints(root, mapping.curvePoints, "Input curve", 0, 1, 0, 1, name + "-curve-points", false);
        }
        void BuildExternalMap(VisualElement root)
        {
            var entries = Instrument.externalParameters; var box = Z.BoxKeyed("External parameters", "Declare external parameter IDs once here; command source tables reference them through pickers. Each ID routes to a typed target on this instrument.", "tracker.external.map");
            box.Add(Button("Add external ID", "Declare a unique external parameter ID.", () => InstrumentEdit("add external parameter", () => { int n = 1; while (entries.Any(e => e.externalId == "Parameter " + n)) n++; entries.Add(new ExternalParameterMapping { externalId = "Parameter " + n, mapping = DefaultMapping(true) }); }, true), "add-external-parameter"));
            foreach (var entry in entries.ToArray()) { int at = entries.IndexOf(entry); var row = Flow(Named(Z.TextInput(entry.externalId, "Declared external ID; duplicates are refused.", v => { if (string.IsNullOrWhiteSpace(v) || entries.Any(e => e != entry && e.externalId == v)) return; RenameExternal(entry, v); }, 170), "external-id-" + at), Button("×", "Remove this external declaration; linked source records are retained as unresolved.", () => InstrumentEdit("remove external parameter", () => entries.Remove(entry), true), "remove-external-" + at)); box.Add(row); BuildMapping(box, entry.mapping, true, "external-mapping-" + at); } root.Add(box);
        }
        void RenameExternal(ExternalParameterMapping entry, string id)
        {
            // Declaration and all same-instrument references form one Undo operation.
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); CompleteUndo(new UnityEngine.Object[] { song, instrument }, "Tracker: external ID"); string old = entry.externalId; entry.externalId = id;
            foreach (var t in Data.tracks) { foreach (var s in Records(t.externalSources).Where(s => s.instrumentId == Instrument.id)) if(s.parameterNumbers!=null)for (int i = 0; i < s.parameterNumbers.Count; i++) if (s.parameterNumbers[i] == old) s.parameterNumbers[i] = id; foreach (var s in Records(t.sourceDevices).Where(s => s.instrumentId == Instrument.id)) foreach (var p in Records(s.parameters)) if (p.externalId == old) p.externalId = id; }
            EditorUtility.SetDirty(song); EditorUtility.SetDirty(instrument); CollapseUndo(group,song,instrument); RefreshLive();
        }
        void BuildPresets(VisualElement root)
        {
            var q = Instrument.parameters; var presets = q.presets;
            root.Add(Flow(Button("New preset", "Snapshot Base settings into a named section preset; Base remains independent.", CreatePreset, "new-instrument-preset"), Button("Remove preset", "Remove the selected preset. Notes using it return to Base; later preset references follow their original sound. Undo restores the preset and all references.", RemovePreset, "remove-instrument-preset")));
            presetIndex = Mathf.Clamp(presetIndex, -1, presets.Count - 1);
            root.Add(Named(Z.MiniRadio(presetIndex + 1, new[] { "Base" }.Concat(presets.Select(p => p?.name ?? "⚠ Missing")).ToArray(), "Choose Base or a preset to author; selecting does not apply it.", v => { presetIndex = v - 1; BuildPane(); }, wrap: true), "instrument-presets"));
            if (presetIndex < 0) return; var preset = presets[presetIndex]; if (preset == null) { root.Add(Diagnostic("Null preset", "Imported null preset is preserved; remove it explicitly to repair playback.")); return; }
            root.Add(Flow(Named(Z.TextInput(preset.name, "Preset display name.", v => InstrumentEdit("preset name", () => preset.name = v), 190), "instrument-preset-name"), Button("Apply to Base", "Resolve enabled sections and replace Base with their independent values. Undo restores the complete previous Base. The preset list is retained.", ApplyPreset, "apply-instrument-preset")));
            var sections = new[] { ("Level", "ovrVolPan", new[] { "volume", "pan" }), ("Sample", "ovrSampleParams", new[] { "baseNote", "fineTune", "baseNoteB", "fineTuneB" }), ("Oscillators", "ovrSynthParams", new[] { "unisonVoices", "unisonSpread" }), ("Blend", "ovrBlend", new[] { "blend" }), ("Pulse", "ovrPulseWidth", new[] { "pulseWidth" }), ("Ratio", "ovrBRatio", new[] { "waveBRatio" }), ("PM", "ovrPMDepth", new[] { "pmDepth" }), ("Detune", "ovrDetune", new[] { "unisonDetune" }), ("Amplitude", "ovrAdsr", new[] { "attack", "decay", "sustain", "release" }), ("Vibrato", "ovrVibrato", new[] { "vibratoDepth", "vibratoRate", "vibratoFadeIn", "vibratoRandomness" }), ("Effects", "ovrEffects", new[] { "instFilterCutoff", "instFilterResonance", "instDelaySend", "instReverbSend" }) };
            foreach (var section in sections)
            {
                var field = preset.GetType().GetField(section.Item2); var box = Z.BoxKeyed(section.Item1, "Override switch controls whether this section participates; disabled values remain authored.", "tracker.preset." + section.Item2); box.Add(InstrumentToggle("Override", (bool)field.GetValue(preset), "Use this section instead of inherited Base.", v => field.SetValue(preset, v))); BuildNumericFields(box, preset, section.Item3, "preset");
                if (section.Item2 == "ovrSampleParams") box.Add(Flow(Z.Field("PCM A", "Preset A PCM override.", Z.Object(preset.sampleClip, "Pick an A AudioClip.", v => InstrumentEdit("preset PCM", () => preset.sampleClip = v), 170)), Z.Field("PCM B", "Preset B PCM override.", Z.Object(preset.sampleClipB, "Pick a B AudioClip.", v => InstrumentEdit("preset B PCM", () => preset.sampleClipB = v), 170))));
                if (section.Item2 == "ovrSynthParams") { box.Add(InstrumentChoice("Wave A", preset.waveA, WaveLabels(q.enumDomain), "Preset wave in Base's wave domain.", v => preset.waveA = v)); box.Add(InstrumentChoice("Wave B", preset.waveB, WaveLabels(q.enumDomain), "Preset secondary wave.", v => preset.waveB = v)); box.Add(InstrumentChoice("Blend", preset.blendMode, new[] { "Mix", "Ring", "Sync", "PM" }, "Preset blend mode.", v => preset.blendMode = v)); }
                if (section.Item2 == "ovrEffects") { box.Add(InstrumentToggle("Filter", preset.instFilterEnabled, "Enable preset filter.", v => preset.instFilterEnabled = v)); box.Add(InstrumentChoice("Filter mode", preset.instFilterMode, new[] { "Low pass", "High pass", "Band pass" }, "Preset filter mode.", v => preset.instFilterMode = v)); }
                foreach (var f in new[] { "blendEnvelopeData", "pulseWidthEnvelopeData", "waveBRatioEnvelopeData", "pmDepthEnvelopeData", "unisonDetuneEnvelopeData" }) { string owner = f.StartsWith("blend") ? "ovrBlend" : f.StartsWith("pulse") ? "ovrPulseWidth" : f.StartsWith("wave") ? "ovrBRatio" : f.StartsWith("pm") ? "ovrPMDepth" : "ovrDetune"; if (owner != section.Item2) continue; var env = preset.GetType().GetField(f); float max = f.StartsWith("pm") ? 65536 : f.StartsWith("wave") ? 64 : f.StartsWith("unison") ? 12000 : 1; BuildRetainedEnvelope(box, "Curve", (ZUIEnvelopeData)env.GetValue(preset), v => env.SetValue(preset, v), 0, max, "preset-" + f); }
                root.Add(box);
            }
        }
        void CreatePreset()
        {
            InstrumentEdit("create preset", () => { var q = ZTrackerMigration.ResolveParameterSets(Instrument)[0].data.parameters; var preset = JsonUtility.FromJson<ZTrackerInstrument.InstrumentPreset>(JsonUtility.ToJson(q)); preset.name = "Preset " + (Instrument.parameters.presets.Count + 1); preset.ovrVolPan = preset.ovrSampleParams = preset.ovrSynthParams = preset.ovrBlend = preset.ovrPulseWidth = preset.ovrBRatio = preset.ovrPMDepth = preset.ovrDetune = preset.ovrAdsr = preset.ovrVibrato = preset.ovrEffects = true; Instrument.parameters.presets.Add(preset); presetIndex = Instrument.parameters.presets.Count - 1; }, true);
        }
        void RemovePreset()
        {
            var presets=Instrument.parameters.presets;if(presetIndex<0||presetIndex>=presets.Count)return;
            int removed=presetIndex;string prefix=Instrument.id+"/preset-";
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Tracker: remove preset");
            CompleteUndo(song!=null?new UnityEngine.Object[]{song,instrument}:new UnityEngine.Object[]{instrument},"Tracker: remove preset");
            if(Data!=null)foreach(var pattern in Data.patterns)foreach(var pt in pattern.tracks)foreach(var line in pt.lines)foreach(var note in line.notes)
            {
                if(note.parameterSetId==null||!note.parameterSetId.StartsWith(prefix,StringComparison.Ordinal)||!int.TryParse(note.parameterSetId.Substring(prefix.Length),out int index))continue;
                if(index==removed)note.parameterSetId="";else if(index>removed&&index<presets.Count)note.parameterSetId=prefix+(index-1);
            }
            presets.RemoveAt(removed);presetIndex=Math.Min(removed,presets.Count-1);Instrument.parameters.activePresetIndex=-1;
            EditorUtility.SetDirty(instrument);if(song!=null)EditorUtility.SetDirty(song);CollapseUndo(group,song,instrument);RefreshLive();BuildPane();
        }
        void ApplyPreset()
        {
            if (presetIndex < 0 || presetIndex >= Instrument.parameters.presets.Count) return;
            InstrumentEdit("apply preset to Base", () => { var old = Instrument; var resolved = ZTrackerMigration.ResolveParameterSets(old)[presetIndex + 1].data; resolved.parameters.presets = old.parameters.presets.Select(p => ZTrackerMigration.Copy(p)).ToList(); resolved.parameters.activePresetIndex = -1; instrument.model = resolved; }, true);
        }
    }
}
