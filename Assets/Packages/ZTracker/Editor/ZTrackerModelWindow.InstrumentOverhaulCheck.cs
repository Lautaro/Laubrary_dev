using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // Disposable assets only. Reflection entry point; no menu or Test Runner.
        public static string CheckInstrumentOverhaulWorkflow()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var window = CreateInstance<ZTrackerModelWindow>(); var songAsset = CreateInstance<ZTrackerSong>(); var ins = CreateInstance<ZTrackerInstrument>();
            try
            {
                ins.schemaVersion = songAsset.schemaVersion = 1; ins.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(ins);
                window.song = songAsset; window.instrument = ins; window.pane = 3; window.Show(); window.position = new Rect(160,120,1400,760); window.CreateGUI();
                void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Panel not attached"); var validate = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); for (int i = 0; i < 4; i++) validate?.Invoke(panel, null); }
                Check("responsive columns change from adjacent to stacked on the attached panel", () =>
                {
                    var host = new VisualElement(); host.style.width = 900; window.rootVisualElement.Add(host);
                    try
                    {
                        var columns = InstrumentColumns(host, "responsive-proof", 340); columns.Add(Z.Text("Left", tooltip: "First column.")); columns.Add(Z.Text("Right", tooltip: "Second column.")); Layout();
                        Need(columns[1].layout.x > columns[0].layout.x + 300 && Math.Abs(columns[1].layout.y - columns[0].layout.y) < 2, "Wide columns did not sit side by side");
                        host.style.width = 320; Layout(); Need(Math.Abs(columns[1].layout.x - columns[0].layout.x) < 2 && columns[1].layout.y > columns[0].layout.y, "Narrow columns did not stack");
                    }
                    finally { host.RemoveFromHierarchy(); }
                });
                Check("actual synth cards and FM operator cards pack horizontally and stack narrow", () =>
                {
                    window.position = new Rect(160,120,1250,800); ins.model.synthMode = SynthMode.Subtractive; window.BuildPane(); Layout();
                    var columns = window.rootVisualElement.Q<VisualElement>("synth-columns"); Need(columns != null && columns.childCount == 2, "Missing actual synth cards");
                    Need(Math.Abs(columns[0].worldBound.y - columns[1].worldBound.y) < 2 && columns[1].worldBound.x > columns[0].worldBound.x + 300, "Actual wide Amplifier wrapped below Oscillators");
                    window.position = new Rect(160,120,760,800); window.BuildPane(); Layout(); columns = window.rootVisualElement.Q<VisualElement>("synth-columns");
                    Need(columns[1].worldBound.y > columns[0].worldBound.y + 20 && Math.Abs(columns[0].worldBound.x - columns[1].worldBound.x) < 2, "Actual narrow synth cards did not stack");
                    window.position = new Rect(160,120,1250,800); ins.model.synthMode = SynthMode.FM; window.BuildPane(); Layout();
                    var operators = window.rootVisualElement.Q<VisualElement>("fm-operators"); Need(operators != null && operators.childCount == 4, "Missing actual FM cards");
                    Need(Math.Abs(operators[0].worldBound.y - operators[1].worldBound.y) < 2 && operators[1].worldBound.x > operators[0].worldBound.x + 300, "Actual wide FM cards leave an empty right column");
                    window.position = new Rect(160,120,760,800); window.BuildPane(); Layout(); operators = window.rootVisualElement.Q<VisualElement>("fm-operators");
                    Need(operators[1].worldBound.y > operators[0].worldBound.y + 20, "Actual narrow FM cards did not stack");
                    ins.model.synthMode = SynthMode.Subtractive; window.position = new Rect(160,120,1250,800); window.BuildPane(); Layout();
                });
                Check("disabled envelope header cannot unfold or discard stages", () =>
                {
                    var q = ins.model.parameters; q.blendEnvelope = false; q.blendAttack = .137f; window.BuildPane();
                    var body = window.rootVisualElement.Q<VisualElement>("synth-blend-envelope-body"); Need(body != null, "Missing envelope body"); var box = body.parent as ZuiBox; Need(box != null, "Missing envelope box");
                    box.IsOpen = true; Need(!box.IsOpen && body.style.display.value == DisplayStyle.None, "Disabled section unfolded");
                    var toggle = box.Q<Toggle>(className: "zui-box__enable"); Need(toggle != null, "Missing real header checkbox"); toggle.value = true; Need(q.blendEnvelope && q.blendAttack == .137f, "Header did not enable preserved envelope");
                    toggle.value = false; Need(!q.blendEnvelope && !box.IsOpen && q.blendAttack == .137f, "Disable lost stages or unfolding gate");
                });
                Check("building Synth and Effects retains legacy filter and missing-destination sends", () =>
                {
                    var q = ins.model.parameters; q.instFilterEnabled = true; q.instFilterCutoff = .24f; q.instFilterResonance = 3.1f; q.instDelaySend = .23f; q.instReverbSend = .37f;
                    string before = JsonUtility.ToJson(ins.model); var synth = new VisualElement(); window.BuildSynth(synth); var effects = new VisualElement(); window.BuildInstrumentChains(effects);
                    Need(before == JsonUtility.ToJson(ins.model), "UI construction mutated authored audio"); Need(synth.Q<VisualElement>("retained-instFilterCutoff") == null, "Standalone synth filter remains"); Need(effects.Q<VisualElement>("retained-instFilterCutoff") != null, "Active compatibility filter is hidden");
                    Need(effects.Q<VisualElement>("synth-instDelaySend") == null && effects.Q<VisualElement>("synth-instReverbSend") == null, "Missing send destination has a live slider");
                    ins.model.fxChains.Add(new AudioEffectChainData()); effects.Clear(); window.BuildInstrumentChains(effects);
                    Need(effects.Q<VisualElement>("instrument-chain-editor")?.GetType() == typeof(Laubrary.Zounds.Uitk.ChainEditorTK), "Instrument is not using the shared chain editor");
                    var send = new TrackData { id = "overhaul-delay", name = "Delay send", kind = TrackKind.Send }; send.devices.nodes.Add(new AudioEffectNodeData { type = Laubrary.Zounds.ZoundEffectType.Delay }); songAsset.model.tracks.Add(send);
                    effects.Clear(); window.BuildInstrumentChains(effects); Need(effects.Q<VisualElement>("synth-instDelaySend") != null && effects.Q<VisualElement>("synth-instReverbSend") == null, "Send sliders are not bound to actual destinations"); songAsset.model.tracks.Remove(send);
                    q.instFilterEnabled = false; q.instDelaySend = q.instReverbSend = 0;
                });
                Check("FM ratios hide for fixed frequency and all four operator envelopes remain", () =>
                {
                    ins.model.synthMode = SynthMode.FM; ins.model.parameters.fmOperators[2].freqFixed = 550; var root = new VisualElement(); window.BuildSynth(root);
                    var ratio = root.Q<VisualElement>("fmOperators.2.freqRatio"); Need(ratio != null, "Missing retained ratio control");
                    bool hidden = false; for (var element = ratio; element != null; element = element.parent) if (element.style.display.value == DisplayStyle.None) hidden = true;
                    Need(hidden, "Fixed-frequency ratio remains ineffective");
                    for (int op = 0; op < 4; op++) Need(root.Q<VisualElement>("fm-adsr-" + op) != null && root.Q<VisualElement>("fmOperators." + op + ".level") != null, "Missing active operator controls " + op);
                });
                Check("FM operator envelope drag edits its stage without rebuilding or losing scroll", () =>
                {
                    ins.model.synthMode = SynthMode.FM; window.position = new Rect(160,120,760,520); window.BuildPane(); Layout();
                    var scroll = window.stage.Q<ScrollView>(); var adsr = window.rootVisualElement.Q<ZuiAdsr>("fm-adsr-2");
                    Need(scroll != null && adsr != null, "Missing attached FM envelope workspace");
                    scroll.scrollOffset = new Vector2(0, 180); Layout(); float beforeScroll = scroll.scrollOffset.y; float beforeAttack = ins.model.parameters.fmOperators[2].attack;
                    var hit = typeof(ZuiAdsr).GetMethod("StageAtLocal", BindingFlags.Instance | BindingFlags.NonPublic);
                    var handle = adsr.HandlePosition(0); var lanePoint = new Vector2(handle.x, handle.y + 45);
                    Need(hit != null && (int)hit.Invoke(adsr, new object[] { lanePoint }) == 0, "Attack lane outside the tiny handle is not editable");
                    adsr.BeginDrag(0); adsr.DragToLocal(handle + new Vector2(24, 0)); adsr.EndDrag(); Layout();
                    Need(ins.model.parameters.fmOperators[2].attack > beforeAttack, "Attack handle did not edit the operator");
                    Need(adsr.panel != null && ReferenceEquals(scroll, window.stage.Q<ScrollView>()), "Envelope edit rebuilt the authoring page");
                    Need(Math.Abs(scroll.scrollOffset.y - beforeScroll) < 2, "Envelope edit moved the authoring scroll from " + beforeScroll + " to " + scroll.scrollOffset.y);
                });
                Check("FM editor passes the expanded mechanical UI audit", () =>
                {
                    window.position = new Rect(160,120,1250,800); ins.model.synthMode = SynthMode.FM; ins.model.parameters.glideEnabled = true; window.BuildPane(); ZuiAudit.ExpandAll(window); Layout();
                    var findings = ZuiAudit.Audit(window, out int foldedSkipped);
                    Need(foldedSkipped == 0, "Audit skipped " + foldedSkipped + " folded sections");
                    Need(findings.Count == 0, string.Join(" | ", findings.Select(f => f.ToString())));
                });
            }
            finally { window.StopPreview(); window.Close(); Undo.ClearUndo(ins); Undo.ClearUndo(songAsset); DestroyImmediate(window); DestroyImmediate(ins); DestroyImmediate(songAsset); }

            using (var left = new NativeArray<float>(1024, Allocator.TempJob)) using (var right = new NativeArray<float>(1024, Allocator.TempJob))
            for (int algorithm = 0; algorithm < 6; algorithm++)
            {
                int route = algorithm;
                Check("FM routing " + (route + 1) + " predicts actual DSP and each operator changes PCM", () =>
                {
                    var asset = CreateInstance<ZTrackerInstrument>(); asset.schemaVersion = 1; asset.model = NewInstrumentData(); asset.model.synthMode = SynthMode.FM;
                    var q = asset.model.parameters; q.type = InstrumentType.FM; q.fmAlgorithm = route; q.fmFeedback = 0; q.volume = .1f; q.attack = q.decay = 0; q.sustain = 1; q.pan = 0;
                    float[] ratios = { 1f, 1.7f, 2.3f, 3.2f }; float[] levels = { .19f, .23f, .31f, .37f };
                    for (int op = 0; op < 4; op++) q.fmOperators[op] = new ZTrackerInstrument.FMOperatorData { freqRatio = ratios[op], level = levels[op], attack = 0, decay = 0, sustain = 1, release = 1 };
                    float[] Render()
                    {
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(asset)))) { engine.SendCommand(TrackerCommand.Audition(0,69)); engine.Render(left,right,1024); return left.ToArray(); }
                    }
                    try
                    {
                        var actual = Render(); var predicted = new double[1024];
                        // Evaluate the displayed directed acyclic graph, independently of the renderer's switch.
                        for (int frame = 0; frame < predicted.Length; frame++)
                        {
                            var signals = new double[4]; for (int op = 0; op < 4; op++)
                            {
                                double modulation = 0; var edges = FmEdges[route]; for (int edge = 0; edge < edges.Length; edge += 2) if (edges[edge+1] == op) modulation += signals[edges[edge]];
                                signals[op] = Math.Sin(2 * Math.PI * (frame * 440d / 48000 * ratios[op] + modulation)) * levels[op];
                            }
                            predicted[frame] = FmCarriers[route].Average(op => signals[op]);
                        }
                        double dot = 0, square = 0; for (int i = 16; i < 1024; i++) { dot += actual[i] * predicted[i]; square += predicted[i] * predicted[i]; }
                        double gain = dot / square, error = 0, energy = 0;
                        for (int i = 16; i < 1024; i++) { error += Math.Pow(actual[i] - predicted[i] * gain, 2); energy += actual[i] * actual[i]; }
                        Need(Math.Abs(gain) > .001 && error / energy < .0001, "Displayed routes do not predict native PCM: normalized error " + error / energy);
                        for (int op = 0; op < 4; op++)
                        {
                            var saved = q.fmOperators[op]; var muted = saved; muted.level = 0; q.fmOperators[op] = muted; var changed = Render(); q.fmOperators[op] = saved;
                            Need(actual.Zip(changed, (a,b) => Math.Abs(a-b)).Sum() > .01, "Operator " + (op+1) + " is not participating in routing " + (route+1));
                        }
                    }
                    finally { DestroyImmediate(asset); }
                });
            }
            return "Instrument overhaul: " + passed + " passed / " + failed + " failed\n" + report;
        }
    }
}
