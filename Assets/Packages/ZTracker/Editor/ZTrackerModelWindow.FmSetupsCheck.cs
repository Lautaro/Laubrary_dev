using System;
using System.Collections.Generic;
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
        // The ten single-carrier FM setups and modulator feedback, measured on rendered PCM and
        // on an attached instrument editor. Disposable assets only; reflection entry point, no
        // menu or Test Runner.
        public static string CheckFmSetups()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            float[] ratios = { 1f, 1.7f, 2.3f, 3.2f }; float[] levels = { .19f, .23f, .31f, .37f };
            ZTrackerInstrument Instrument(int algorithm, float[] feedback = null, float[] ratio = null, float[] level = null)
            {
                var asset = CreateInstance<ZTrackerInstrument>(); asset.schemaVersion = 1; asset.model = NewInstrumentData(); asset.model.synthMode = SynthMode.FM;
                var q = asset.model.parameters; q.type = InstrumentType.FM; q.fmAlgorithm = algorithm; q.fmFeedback = 0; q.volume = .1f; q.attack = q.decay = 0; q.sustain = 1; q.pan = 0;
                // The route models below are per output sample; oversampling (T-0645, its own check) is switched off here.
                q.fmSingleRate = true;
                for (int op = 0; op < 4; op++) q.fmOperators[op] = new ZTrackerInstrument.FMOperatorData { freqRatio = (ratio ?? ratios)[op], level = (level ?? levels)[op], attack = 0, decay = 0, sustain = 1, release = 1, feedback = feedback != null && op < 3 ? feedback[op] : 0 };
                return asset;
            }
            float[] Render(ZTrackerInstrument asset, int frames = 4096, int rate = 48000, Action<TrackerOffline, int> during = null)
            {
                var output = new float[frames];
                using (var left = new NativeArray<float>(256, Allocator.TempJob)) using (var right = new NativeArray<float>(256, Allocator.TempJob))
                using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(asset), rate)))
                {
                    engine.SendCommand(TrackerCommand.Audition(0, 69));
                    for (int at = 0; at < frames; at += 256) { during?.Invoke(engine, at); int n = Math.Min(256, frames - at); engine.Render(left, right, n, 256); for (int i = 0; i < n; i++) output[at + i] = left[i]; }
                }
                return output;
            }
            double Difference(float[] a, float[] b, int from = 0) { double sum = 0; for (int i = from; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]); return sum; }
            bool Identical(float[] a, float[] b, int from = 0) { for (int i = from; i < a.Length; i++) if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false; return true; }
            // Magnitudes at the first harmonics of the played note (Goertzel, Hann window).
            double[] Harmonics(float[] x, int rate, int count = 16, int from = 2048)
            {
                int n = x.Length - from; var result = new double[count];
                for (int k = 1; k <= count; k++)
                {
                    double w = 2 * Math.PI * 440d * k / rate, re = 0, im = 0;
                    for (int i = 0; i < n; i++) { double win = .5 - .5 * Math.Cos(2 * Math.PI * i / (n - 1)); re += x[from + i] * win * Math.Cos(w * i); im -= x[from + i] * win * Math.Sin(w * i); }
                    result[k - 1] = Math.Sqrt(re * re + im * im);
                }
                return result;
            }
            var made = new List<UnityEngine.Object>();
            ZTrackerInstrument Make(int algorithm, float[] feedback = null, float[] ratio = null, float[] level = null) { var a = Instrument(algorithm, feedback, ratio, level); made.Add(a); return a; }
            try
            {
                for (int s = 1; s <= TrackerFmSetups.Count; s++)
                {
                    int setup = s;
                    Check("setup " + setup + " (" + TrackerFmSetups.Formula(setup) + ") renders its drawn routes; used operators change the sound, unused ones do not", () =>
                    {
                        var asset = Make(TrackerFmSetups.Base + setup); var q = asset.model.parameters; var actual = Render(asset);
                        var edges = TrackerFmSetups.Edges(setup); var predicted = new double[actual.Length];
                        for (int frame = 0; frame < predicted.Length; frame++)
                        {
                            var signals = new double[4];
                            for (int op = 0; op < 4; op++)
                            {
                                double modulation = 0; for (int e = 0; e < edges.Length; e += 2) if (edges[e + 1] == op) modulation += signals[edges[e]];
                                signals[op] = Math.Sin(2 * Math.PI * (frame * 440d / 48000 * ratios[op] + modulation)) * levels[op];
                            }
                            predicted[frame] = signals[TrackerFmSetups.Carrier];
                        }
                        double dot = 0, square = 0; for (int i = 16; i < actual.Length; i++) { dot += actual[i] * predicted[i]; square += predicted[i] * predicted[i]; }
                        double gain = dot / square, error = 0, energy = 0;
                        for (int i = 16; i < actual.Length; i++) { error += Math.Pow(actual[i] - predicted[i] * gain, 2); energy += actual[i] * actual[i]; }
                        Need(Math.Abs(gain) > .001 && error / energy < .0001, "Drawn routes do not predict the rendered PCM: normalized error " + error / energy);
                        for (int op = 0; op < 4; op++)
                        {
                            var saved = q.fmOperators[op]; var muted = saved; muted.level = 0; muted.freqRatio = 5.5f; q.fmOperators[op] = muted; var changed = Render(asset); q.fmOperators[op] = saved;
                            if (TrackerFmSetups.Uses(setup, op)) Need(Difference(actual, changed) > .01, TrackerFmSetups.Letter(op) + " is used by setup " + setup + " but does not change its sound");
                            else Need(Identical(actual, changed), TrackerFmSetups.Letter(op) + " is not part of setup " + setup + " but changed its sound");
                        }
                    });
                    Check("setup " + setup + ": each used modulator's feedback changes the spectrum; feedback on an unused one changes nothing", () =>
                    {
                        var plain = Render(Make(TrackerFmSetups.Base + setup)); var plainSpectrum = Harmonics(plain, 48000);
                        for (int op = 0; op < TrackerFmSetups.Modulators; op++)
                        {
                            var fb = new float[3]; fb[op] = .8f; var fed = Render(Make(TrackerFmSetups.Base + setup, fb));
                            if (!TrackerFmSetups.Uses(setup, op)) { Need(Identical(plain, fed), "Feedback on unused " + TrackerFmSetups.Letter(op) + " changed setup " + setup); continue; }
                            var spectrum = Harmonics(fed, 48000); double distance = 0, total = 0;
                            for (int k = 0; k < spectrum.Length; k++) { distance += Math.Abs(spectrum[k] - plainSpectrum[k]); total += plainSpectrum[k]; }
                            Need(distance / total > .02, "Feedback on " + TrackerFmSetups.Letter(op) + " barely changed setup " + setup + "'s spectrum (" + (distance / total).ToString("P2") + ")");
                        }
                    });
                }
                Check("feedback brightens: A's feedback adds upper harmonics to A > C", () =>
                {
                    // A light modulation index keeps C's sidebands a near-linear copy of A's own
                    // spectrum, so A's added harmonics show up directly above the second harmonic.
                    float[] ones = { 1, 1, 1, 1 }, lv = { .05f, 0, 0, .9f };
                    double Upper(float[] x) { var h = Harmonics(x, 48000); double up = 0, all = 0; for (int k = 0; k < h.Length; k++) { all += h[k] * h[k]; if (k >= 2) up += h[k] * h[k]; } return up / all; }
                    double before = Upper(Render(Make(TrackerFmSetups.Base + 1, null, ones, lv))), after = Upper(Render(Make(TrackerFmSetups.Base + 1, new[] { .9f, 0, 0 }, ones, lv)));
                    report.AppendLine("MEASURE A>C energy above the 2nd harmonic: no feedback=" + before.ToString("E2") + " feedback 90%=" + after.ToString("E2"));
                    Need(after > before * 10, "Feedback did not add upper harmonics: " + before + " -> " + after);
                });
                Check("feedback timbre does not depend on the sample rate (44.1, 48 and 96 kHz harmonics agree)", () =>
                {
                    float[] ones = { 1, 1, 1, 1 }, lv = { .25f, 0, 0, .9f };
                    var asset = Make(TrackerFmSetups.Base + 1, new[] { .7f, 0, 0 }, ones, lv); var reference = Harmonics(Render(asset, 8192, 48000), 48000); double r0 = reference[0];
                    foreach (int rate in new[] { 44100, 96000 })
                    {
                        var h = Harmonics(Render(asset, rate == 96000 ? 16384 : 8192, rate), rate, 16, rate == 96000 ? 4096 : 2048); double worst = 0;
                        for (int k = 0; k < 8; k++) worst = Math.Max(worst, Math.Abs(h[k] / h[0] - reference[k] / r0));
                        report.AppendLine("MEASURE feedback harmonic-ratio difference 48k vs " + rate + "=" + worst.ToString("F4"));
                        Need(worst < .03, "Harmonic balance at " + rate + " Hz differs by " + worst);
                    }
                });
                Check("zero feedback is bit-identical to no feedback, also after feedback is turned off on a playing note", () =>
                {
                    var none = Render(Make(TrackerFmSetups.Base + 8), 12288);
                    var zero = Make(TrackerFmSetups.Base + 8, new[] { 0f, 0f, 0f }); Need(Identical(none, Render(zero, 12288)), "Explicit 0 differs from no feedback");
                    var fed = Make(TrackerFmSetups.Base + 8, new[] { .5f, .6f, .7f });
                    var off = Render(fed, 12288, 48000, (e, at) => { if (at == 1024) for (int op = 0; op < 3; op++) e.SendCommand(TrackerCommand.SetParameter(0, TrackerParameter.Op0Feedback + op, 0)); });
                    Need(Difference(none, off, 0) > .01, "Feedback had no effect before it was turned off");
                    Need(Identical(none, off, 1024 + 4096), "After the 5 ms glide to 0, output still differs from no feedback");
                });
                Check("a feedback change on a held note glides without a click", () =>
                {
                    float[] ones = { 1, 1, 1, 1 }, lv = { .4f, .4f, 0, .9f };
                    var asset = Make(TrackerFmSetups.Base + 2, null, ones, lv);
                    var x = Render(asset, 8192, 48000, (e, at) => { if (at == 4096) e.SendCommand(TrackerCommand.SetParameter(0, TrackerParameter.Op1Feedback, 1)); });
                    double Jump(int from, int to) { double m = 0; for (int i = from + 1; i < to; i++) m = Math.Max(m, Math.Abs(x[i] - x[i - 1])); return m; }
                    double settledBefore = Jump(3072, 4096), change = Jump(4096, 4608), settledAfter = Jump(7168, 8192);
                    report.AppendLine("MEASURE largest sample step before=" + settledBefore.ToString("F5") + " during change=" + change.ToString("F5") + " after=" + settledAfter.ToString("F5"));
                    Need(change <= Math.Max(settledBefore, settledAfter) * 1.25, "Changing feedback while playing produced a step of " + change);
                });
                Check("full feedback on every modulator stays finite and bounded in the densest setup", () =>
                {
                    var x = Render(Make(TrackerFmSetups.Base + 10, new[] { 1f, 1f, 1f }, null, new[] { 1f, 1f, 1f, 1f }), 48000);
                    Need(x.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), "Non-finite output"); Need(x.Max(v => Math.Abs(v)) <= .11f, "Output exceeded the carrier's level: " + x.Max(v => Math.Abs(v)));
                    // Oversampled (the default since T-0645): the half-band filter rings a little on this noise-like sound.
                    var dense = Make(TrackerFmSetups.Base + 10, new[] { 1f, 1f, 1f }, null, new[] { 1f, 1f, 1f, 1f }); dense.model.parameters.fmSingleRate = false;
                    var y = Render(dense, 48000); float peak = y.Max(v => Math.Abs(v));
                    report.AppendLine("MEASURE densest setup at full feedback: peak " + x.Max(v => Math.Abs(v)).ToString("0.0000") + " at the output rate, " + peak.ToString("0.0000") + " oversampled (carrier level 0.1)");
                    Need(y.All(v => !float.IsNaN(v) && !float.IsInfinity(v)) && peak <= .14f, "Oversampled output not bounded: " + peak);
                });
                Check("the setup path runs in Burst and allocates nothing while feedback changes", () =>
                {
                    var asset = Make(TrackerFmSetups.Base + 8, new[] { .3f, .3f, .3f });
                    using (var left = new NativeArray<float>(1024, Allocator.TempJob)) using (var right = new NativeArray<float>(1024, Allocator.TempJob))
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(asset))))
                    {
                        e.SendCommand(TrackerCommand.Audition(0, 69)); e.Render(left, right, 1024); Need(e.Compiled, "Render ran managed");
                        long managed = AudioThreadGuard.ManagedBlocks; for (int i = 0; i < 4; i++) e.Render(left, right, 1024);
                        var command = TrackerCommand.SetParameter(0, TrackerParameter.Op2Feedback, .9f);
                        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 20; i++) { if (i == 10) e.SendCommand(command); e.Render(left, right, 1024); } long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                        Need(AudioThreadGuard.ManagedBlocks == managed, "Managed render blocks"); report.AppendLine("MEASURE setupRenderAllocatedBytes=" + allocated);
                        Need(allocated == 0, "Allocated " + allocated + " bytes while rendering");
                    }
                });
                Check("older Stack and Merge keep their exact older output and match setups 4 and 7", () =>
                {
                    foreach (var pair in new[] { (0, 4), (2, 7) })
                    {
                        var older = Render(Make(pair.Item1)); var setup = Render(Make(TrackerFmSetups.Base + pair.Item2)); double worst = 0;
                        for (int i = 0; i < older.Length; i++) worst = Math.Max(worst, Math.Abs(older[i] - setup[i]));
                        report.AppendLine("MEASURE older " + pair.Item1 + " vs setup " + pair.Item2 + " largest difference=" + worst.ToString("E2"));
                        Need(worst < 1e-5, "Older routing " + pair.Item1 + " differs from setup " + pair.Item2 + " by " + worst);
                        // Feedback on an older Stack/Merge instrument plays it as the setup.
                        var fb = new[] { .4f, .5f, .6f };
                        Need(Identical(Render(Make(pair.Item1, fb)), Render(Make(TrackerFmSetups.Base + pair.Item2, fb))), "Older routing " + pair.Item1 + " with feedback is not played as setup " + pair.Item2);
                    }
                });
                Check("mapping and parameter names resolve for modulator feedback", () =>
                {
                    for (int op = 0; op < 3; op++) Need(TrackerParameters.Resolve("fmOperators." + op + ".feedback", out var p) && p == TrackerParameter.Op0Feedback + op, "fmOperators." + op + ".feedback did not resolve");
                    Need(!TrackerParameters.Resolve("fmOperators.3.feedback", out _), "The carrier has no feedback parameter");
                    Need(TrackerParameters.Clamp(TrackerParameter.Op1Feedback, 3) == 1 && TrackerParameters.Units(TrackerParameter.Op1Feedback) == "normalized", "Feedback range or units");
                    Need(ParameterName(TrackerParameter.Op2Feedback) == "fmOperators.2.feedback", "Destination name");
                    Need(TrackerParameters.Clamp(TrackerParameter.Op3Release, 5) == 5, "Operator fields moved");
                });
            }
            finally { foreach (var o in made) DestroyImmediate(o); }

            // The real instrument editor, attached and laid out.
            var window = CreateInstance<ZTrackerModelWindow>(); var songAsset = CreateInstance<ZTrackerSong>(); var ins = CreateInstance<ZTrackerInstrument>();
            try
            {
                ins.schemaVersion = songAsset.schemaVersion = 1; ins.model = NewInstrumentData(); ins.model.synthMode = SynthMode.FM; ins.model.parameters.type = InstrumentType.FM; songAsset.model = TrackerEngineCheck.FixtureSongNew(ins);
                window.song = songAsset; window.instrument = ins; window.pane = 3; window.Show(); window.position = new Rect(160,120,1250,800); window.CreateGUI();
                void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Panel not attached"); var validate = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); for (int i = 0; i < 4; i++) validate?.Invoke(panel, null); }
                var q = ins.model.parameters;
                foreach (var size in new[] { new Vector2(1250, 800), new Vector2(760, 800) })
                {
                    Check("every setup is picked by a click at " + size.x + "px and shows only its operators and diagram", () =>
                    {
                        Undo.ClearUndo(ins); window.position = new Rect(160,120,size.x,size.y); q.fmAlgorithm = 0; window.BuildPane(); Layout();
                        Need(window.rootVisualElement.Q<VisualElement>("fm-setup-4").ClassListContains("fm-setup-selected"), "Older Stack is not shown as setup 4");
                        for (int pass = 0; pass < 2; pass++)
                        for (int s = 1; s <= TrackerFmSetups.Count; s++)
                        {
                            var tile = window.rootVisualElement.Q<VisualElement>("fm-setup-" + s); Need(tile != null, "Missing tile " + s);
                            var picker = window.rootVisualElement.Q<VisualElement>("fm-setups");
                            Need(tile.worldBound.xMin >= picker.worldBound.xMin - 1 && tile.worldBound.xMax <= picker.worldBound.xMax + 1, "Tile " + s + " is clipped at " + size.x + "px");
                            if (q.fmAlgorithm != TrackerFmSetups.Base + s) { PointerClick(tile); Layout(); }
                            Need(q.fmAlgorithm == TrackerFmSetups.Base + s, "Click did not pick setup " + s);
                            var root = window.rootVisualElement; var cards = root.Q<VisualElement>("fm-operators");
                            int used = Enumerable.Range(0, 4).Count(o => TrackerFmSetups.Uses(s, o));
                            Need(cards.childCount == used, "Setup " + s + " shows " + cards.childCount + " operator cards, uses " + used);
                            for (int o = 0; o < 4; o++)
                            {
                                bool uses = TrackerFmSetups.Uses(s, o);
                                Need((root.Q<VisualElement>("fm-adsr-" + o) != null) == uses, "Operator " + TrackerFmSetups.Letter(o) + " card visibility wrong in setup " + s);
                                Need((root.Q<VisualElement>("fm-routing-node-" + o) != null) == uses, "Diagram node " + TrackerFmSetups.Letter(o) + " wrong in setup " + s);
                                Need((root.Q<VisualElement>("fmOperators." + o + ".feedback") != null) == (uses && o < 3), "Feedback control for " + TrackerFmSetups.Letter(o) + " wrong in setup " + s);
                            }
                            var graph = root.Q<VisualElement>("fm-routing");
                            for (int o = 0; o < 4; o++) { var node = root.Q<VisualElement>("fm-routing-node-" + o); if (node == null) continue; Need(node.worldBound.xMin >= graph.worldBound.xMin - 1 && node.worldBound.xMax <= graph.worldBound.xMax + 1 && node.worldBound.yMin >= graph.worldBound.yMin - 1 && node.worldBound.yMax <= graph.worldBound.yMax + 1, "Diagram node clipped in setup " + s); }
                            Need(root.Q<VisualElement>("synth-fmFeedback") == null, "Older Output feedback shown for a setup with none");
                        }
                        Undo.PerformUndo(); Need(q.fmAlgorithm == TrackerFmSetups.Base + 9, "Undo did not restore the previous setup: " + q.fmAlgorithm);
                        Undo.PerformRedo(); Need(q.fmAlgorithm == TrackerFmSetups.Base + 10, "Redo did not restore setup 10");
                    });
                }
                Check("modulator feedback has a control on each used modulator only, and its edits store and undo", () =>
                {
                    Undo.ClearUndo(ins); window.position = new Rect(160,120,1250,800); q.fmAlgorithm = TrackerFmSetups.Base + 6; window.BuildPane(); Layout();
                    var host = window.rootVisualElement.Q<VisualElement>("fmOperators.1.feedback"); Need(host != null, "No feedback control on B");
                    var value = host as ZuiValueControl ?? host.Q<ZuiValueControl>(); Need(value != null, "Feedback is not a value control");
                    Need(window.rootVisualElement.Q<VisualElement>("fmOperators.3.feedback") == null, "The carrier shows a feedback control");
                    window.InstrumentEdit("operator feedback", () => { var o = q.fmOperators[1]; o.feedback = .42f; q.fmOperators[1] = o; });
                    Need(Math.Abs(q.fmOperators[1].feedback - .42f) < 1e-6, "Edit not stored");
                    Undo.PerformUndo(); Need(q.fmOperators[1].feedback == 0, "Undo did not restore 0 feedback");
                });
                Check("older multi-carrier routings stay visible and editable", () =>
                {
                    q.fmAlgorithm = 1; q.fmFeedback = .3f; window.BuildPane(); Layout(); var root = window.rootVisualElement;
                    Need(root.Q<VisualElement>("fm-operators").childCount == 4, "Older Pairs routing lost its four operators");
                    Need(root.Q<VisualElement>("synth-fmFeedback") != null, "Older Output feedback hidden");
                    Need(root.Q<VisualElement>("fm-routing-node-4") != null, "Older diagram missing its Output node");
                    Need(Enumerable.Range(1, 10).All(s => !root.Q<VisualElement>("fm-setup-" + s).ClassListContains("fm-setup-selected")), "A setup is shown as selected for an older multi-carrier routing");
                    q.fmFeedback = 0; q.fmAlgorithm = TrackerFmSetups.Base + 1;
                });
            }
            finally { window.StopPreview(); window.Close(); Undo.ClearUndo(ins); Undo.ClearUndo(songAsset); DestroyImmediate(window); DestroyImmediate(ins); DestroyImmediate(songAsset); }
            return "FM setups: " + passed + " passed / " + failed + " failed\n" + report;
        }
    }
}
