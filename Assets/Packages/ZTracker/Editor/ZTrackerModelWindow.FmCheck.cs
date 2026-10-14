using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // Disposable in-memory assets and an attached window. Run from the canonical editor;
        // this is not a menu item or a Unity Test Runner test.
        public static string CheckFmSurface()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action)
            {
                try { action(); passed++; report.AppendLine("PASS " + title); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); }
            }
            var window = CreateInstance<ZTrackerModelWindow>();
            var songAsset = CreateInstance<ZTrackerSong>();
            var instrumentAsset = CreateInstance<ZTrackerInstrument>();
            try
            {
                instrumentAsset.schemaVersion = songAsset.schemaVersion = 1;
                instrumentAsset.model = NewInstrumentData();
                instrumentAsset.model.synthMode = SynthMode.FM;
                instrumentAsset.model.parameters.type = InstrumentType.FM;
                songAsset.model = TrackerEngineCheck.FixtureSongNew(instrumentAsset);
                window.song = songAsset; window.instrument = instrumentAsset; window.pane = 3;
                window.Show(); window.position = new Rect(160, 120, 1100, 760); window.CreateGUI();
                void Layout()
                {
                    var panel = window.rootVisualElement.panel;
                    Need(panel != null, "FM fixture has no attached panel");
                    var validate = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    for (int n = 0; n < 4; n++) validate?.Invoke(panel, null);
                }
                void Click(string name)
                {
                    var button = window.rootVisualElement.Q<Button>(name);
                    Need(button != null, "Missing action " + name);
                    typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(button.clickable, new object[] { null, 0 });
                }
                Check("legacy FM without operators offers Undo-safe initialization", () =>
                {
                    Undo.ClearUndo(instrumentAsset);
                    var parameters = instrumentAsset.model.parameters;
                    parameters.fmOperators = null;
                    string before = JsonUtility.ToJson(instrumentAsset.model);
                    window.BuildPane(); Layout();
                    Need(before == JsonUtility.ToJson(instrumentAsset.model), "Building FM silently changed the legacy instrument");
                    Need(window.rootVisualElement.Q<Button>("fm-create-operators") != null, "No visible way to create the missing operators");
                    Click("fm-create-operators"); Layout();
                    Need(parameters.fmOperators?.Length == 4, "FM repair did not initialize four operators");
                    Need(window.rootVisualElement.Q<VisualElement>("fm-operators")?.childCount == 4, "Initialized operators are not editable");
                    Undo.PerformUndo(); Need(instrumentAsset.model.parameters.fmOperators == null, "Undo did not restore the legacy array");
                    Undo.PerformRedo(); Need(instrumentAsset.model.parameters.fmOperators?.Length == 4, "Redo did not restore four operators");
                });
                Check("partial and extended FM arrays retain authored operator data", () =>
                {
                    Undo.ClearUndo(instrumentAsset);
                    var parameters = instrumentAsset.model.parameters;
                    var authored = new ZTrackerInstrument.FMOperatorData { freqRatio = 3.25f, level = .72f, attack = .17f, sustain = .38f };
                    parameters.fmOperators = new[] { authored };
                    window.BuildPane(); Click("fm-create-operators");
                    Need(parameters.fmOperators.Length == 4 && parameters.fmOperators[0].freqRatio == authored.freqRatio && parameters.fmOperators[0].attack == authored.attack, "Repair overwrote the authored first operator");
                    var defaults = NewInstrumentData().parameters.fmOperators;
                    Need(parameters.fmOperators[1].freqRatio == defaults[1].freqRatio && parameters.fmOperators[3].level == defaults[3].level, "Missing slots did not use declared defaults");
                    parameters.fmOperators = Enumerable.Repeat(authored, 5).ToArray();
                    window.BuildPane();
                    Need(parameters.fmOperators.Length == 5 && window.rootVisualElement.Q<VisualElement>("fm-operators")?.childCount == 4, "Imported extra operator data was lost or blocked editing");
                });
                Check("routing labels stay legible inside narrow and wide attached views", () =>
                {
                    var host = new VisualElement(); window.rootVisualElement.Add(host);
                    try
                    {
                        foreach (float width in new[] { 240f, 800f })
                        for (int algorithm = 0; algorithm < 6; algorithm++)
                        {
                            host.style.width = width; host.Clear(); var graph = new FmRoutingView(algorithm); host.Add(graph); Layout();
                            Need(Math.Abs(graph.worldBound.width - width) < 2, "Graph did not fit the " + width + "px host");
                            Rect previous = default;
                            for (int node = 0; node < 5; node++)
                            {
                                var label = graph.Q<VisualElement>("fm-routing-node-" + node); Need(label != null, "Missing operator/output node");
                                var bounds = label.worldBound;
                                Need(bounds.xMin >= graph.worldBound.xMin - 1 && bounds.xMax <= graph.worldBound.xMax + 1 && bounds.yMin >= graph.worldBound.yMin && bounds.yMax < graph.worldBound.yMax, "Node clipped at " + width + "px");
                                if (node > 0) Need(bounds.xMin >= previous.xMax, "Operator/output labels overlap at " + width + "px");
                                Need(label.tooltip.Contains(node == 4 || FmCarriers[algorithm].Contains(node) ? "carrier" : "modulator"), "Node role is not explained");
                                previous = bounds;
                            }
                            var feedback = graph.Q<VisualElement>("fm-routing-feedback");
                            Need(feedback != null, "Feedback label missing");
                            Need(feedback.worldBound.yMax <= graph.worldBound.yMax,
                                "Feedback label clipped: width=" + width + " algorithm=" + algorithm + " graph=" + graph.worldBound + " feedback=" + feedback.worldBound);
                        }
                    }
                    finally { host.RemoveFromHierarchy(); }
                });
            }
            finally
            {
                window.StopPreview(); window.Close(); Undo.ClearUndo(instrumentAsset); Undo.ClearUndo(songAsset);
                DestroyImmediate(window); DestroyImmediate(instrumentAsset); DestroyImmediate(songAsset);
            }
            report.Append("FM surface: ").Append(passed).Append(" passed, ").Append(failed).Append(" failed");
            return report.ToString();
        }
    }
}
