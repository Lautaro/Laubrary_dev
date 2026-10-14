using System;
using System.Linq;
using System.Reflection;
using System.Text;
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
        /// <summary>Exercises the Synth pitch controls on a disposable, attached window without touching MIDI preferences.</summary>
        public static string CheckPitchControls()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>();
            var songAsset = CreateInstance<ZTrackerSong>();
            var instrumentAsset = CreateInstance<ZTrackerInstrument>();
            void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            void Check(string title, Action action)
            {
                try { action(); passed++; report.AppendLine("PASS " + title); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); }
            }

            instrumentAsset.schemaVersion = songAsset.schemaVersion = 1;
            instrumentAsset.model = NewInstrumentData();
            songAsset.model = TrackerEngineCheck.FixtureSongNew(instrumentAsset);
            window.song = songAsset; window.instrument = instrumentAsset; window.pane = 3;
            window.midiSimulatedOnly = true;
            window.position = new Rect(120, 120, 1250, 800);
            try
            {
                window.Show(); window.position = new Rect(120, 120, 1250, 800); window.CreateGUI();
                void Layout()
                {
                    var panel = window.rootVisualElement.panel;
                    Need(panel != null, "Pitch fixture has no attached panel");
                    var validate = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    for (int i = 0; i < 4; i++) validate?.Invoke(panel, null);
                }
                bool Visible(VisualElement element)
                {
                    for (var current = element; current != null; current = current.parent)
                        if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == UnityEngine.UIElements.Visibility.Hidden) return false;
                    return element.panel != null;
                }
                VisualElement Find(string name)
                {
                    var element = window.rootVisualElement.Q<VisualElement>(name);
                    Need(element != null, "Missing control " + name);
                    return element;
                }
                VisualElement FlowItem(VisualElement element)
                {
                    for (var current = element; current != null; current = current.parent)
                        if (current.parent != null && current.parent.ClassListContains("tracker-flow")) return current;
                    throw new InvalidOperationException("Control is not inside a ZTracker flow row: " + element.name);
                }
                void ChooseMode(string label)
                {
                    var mode = Find("synth-note-mode");
                    var button = mode.Query<Button>().ToList().FirstOrDefault(b => b.text == label);
                    Need(button != null && Visible(button), "Missing visible Notes choice " + label);
                    typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Invoke(button.clickable, new object[] { null, 0 });
                    Layout();
                }
                void NeedModeSelected(string label)
                {
                    var mode = Find("synth-note-mode");
                    var selected = mode.Query<Button>().ToList().Where(b => b.ClassListContains("zui-radio__on") || b.ClassListContains("zui-segmented__on")).ToList();
                    Need(selected.Count == 1 && selected[0].text == label, "Notes choice did not restore " + label + " after rebuild");
                }
                void SetSlider(string name, float value)
                {
                    var control = Find(name);
                    var slider = control as ZuiMicroSlider ?? control.Q<ZuiMicroSlider>();
                    Need(slider != null && Visible(slider), "Missing visible scalar " + name);
                    typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(slider, new object[] { value, true });
                    Layout();
                }
                var q = instrumentAsset.model.parameters;

                Check("Notes choices set independent Glide and Legato bits, preserve time, and Undo/Redo after rebuild", () =>
                {
                    q.glideEnabled = false; q.legatoEnabled = false; q.glideLegato = false; q.glideSeconds = .37f;
                    Undo.ClearUndo(instrumentAsset); window.BuildPane(); Layout();
                    Need(!Visible(Find("synth-glideSeconds")), "Glide time should be hidden in None mode (it does nothing there)");
                    Need(window.rootVisualElement.Q("synth-legato-velocity") == null, "Legato velocity shown in None mode");
                    ChooseMode("Glide");
                    var mode = FlowItem(Find("synth-note-mode")); var time = FlowItem(Find("synth-glideSeconds"));
                    bool sameFlow = ReferenceEquals(mode.parent, time.parent);
                    bool sameY = Math.Abs(mode.worldBound.yMin - time.worldBound.yMin) < 2;
                    bool noOverlap = mode.worldBound.xMax <= time.worldBound.xMin + 1;
                    Need(sameFlow && sameY && noOverlap,
                        "Notes/time geometry: sameFlow=" + sameFlow + " sameY=" + sameY + " noOverlap=" + noOverlap +
                        "; window=" + window.position + " root=" + window.rootVisualElement.worldBound +
                        "; synthColumns=" + Find("synth-columns").worldBound +
                        "; flow=" + (mode.parent != null ? mode.parent.worldBound.ToString() : "<none>") +
                        "; modeItem=" + mode.worldBound + " modeControl=" + Find("synth-note-mode").worldBound +
                        "; timeItem=" + time.worldBound + " timeControl=" + Find("synth-glideSeconds").worldBound);
                    Need(q.glideEnabled && !q.legatoEnabled && Math.Abs(q.glideSeconds - .37f) < .00001f, "Glide choice changed the wrong flags or glide time");
                    Need(Visible(Find("synth-glideSeconds")) && Find("synth-glideSeconds").enabledSelf, "Glide time not shown with Glide mode");
                    ChooseMode("Legato");
                    Need(!q.glideEnabled && q.legatoEnabled && q.legatoSlides && Math.Abs(q.glideSeconds - .37f) < .00001f, "Legato choice changed Glide or its time, or did not make legato slide");
                    Need(Visible(Find("synth-glideSeconds")) && Find("synth-glideSeconds").enabledSelf, "Glide time (the legato slide time) not shown in Legato mode");
                    Need(Visible(Find("synth-legato-velocity")), "Legato velocity not shown in Legato mode");
                    ChooseMode("Both");
                    Need(q.glideEnabled && q.legatoEnabled && Math.Abs(q.glideSeconds - .37f) < .00001f, "Both choice did not set both flags while preserving time");
                    Undo.PerformUndo(); Layout();
                    Need(!q.glideEnabled && q.legatoEnabled && Math.Abs(q.glideSeconds - .37f) < .00001f, "Undo after rebuild did not restore Legato-only state");
                    NeedModeSelected("Legato");
                    Undo.PerformRedo(); Layout();
                    Need(q.glideEnabled && q.legatoEnabled && Math.Abs(q.glideSeconds - .37f) < .00001f, "Redo after rebuild did not restore Both state");
                    NeedModeSelected("Both");
                    ChooseMode("None");
                    Need(!q.glideEnabled && !q.legatoEnabled && Math.Abs(q.glideSeconds - .37f) < .00001f, "None choice changed glide time or left a mode enabled");
                });

                Check("Pitch and all Vibrato controls remain directly visible and editable at zero depth", () =>
                {
                    q.vibratoDepth = 0; q.vibratoRate = 5; q.vibratoFadeIn = 0; q.vibratoRandomness = 0;
                    window.BuildPane(); Layout();
                    foreach (var name in new[] { "synth-fineTune", "synth-transpose", "synth-vibratoDepth", "synth-vibratoRate", "synth-vibratoFadeIn", "synth-vibratoRandomness" })
                        Need(Visible(Find(name)), name + " is hidden at zero Vibrato depth");
                    SetSlider("synth-vibratoRate", 7);
                    SetSlider("synth-vibratoFadeIn", 480);
                    SetSlider("synth-vibratoRandomness", 35);
                    SetSlider("synth-vibratoDepth", 25);
                    SetSlider("synth-vibratoDepth", 0);
                    Need(Math.Abs(q.vibratoRate - 7) < .00001f && Math.Abs(q.vibratoFadeIn - .48f) < .00001f && Math.Abs(q.vibratoRandomness - .35f) < .00001f && q.vibratoDepth == 0,
                        "Vibrato controls were not independently editable at zero depth");
                });

                Check("Tune cents and semitone steps share a row and edit independent stored values", () =>
                {
                    q.fineTune = 17.25f; q.transpose = 4; window.BuildPane(); Layout();
                    var cents = FlowItem(Find("synth-fineTune")); var steps = FlowItem(Find("synth-transpose"));
                    Need(ReferenceEquals(cents.parent, steps.parent), "Tune cents and Tune steps are not in the same row");
                    Need(Math.Abs(cents.worldBound.yMin - steps.worldBound.yMin) < 2 && cents.worldBound.xMax <= steps.worldBound.xMin + 1,
                        "Tune controls do not fit side-by-side in the wide Synth layout");
                    SetSlider("synth-fineTune", 38);
                    Need(Math.Abs(q.fineTune - 38) < .00001f && q.transpose == 4, "Cents edit changed semitone steps or stored the wrong cents");
                    SetSlider("synth-transpose", 9);
                    Need(q.transpose == 9 && Math.Abs(q.fineTune - 38) < .00001f, "Semitone-step edit changed cents or stored the wrong steps");
                });

                Check("Transpose double-click resets to its declared default with Undo/Redo", () =>
                {
                    Undo.ClearUndo(instrumentAsset); q.transpose = 7; window.BuildPane(); Layout();
                    var slider = Find("synth-transpose");
                    var sliderRoot = slider.panel.visualTree; var sliderAt = slider.worldBound.center;
                    using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = sliderAt }))
                    { down.target = slider; slider.SendEvent(down); }
                    PointerRelease(sliderRoot, sliderAt);
                    Layout();
                    Need(q.transpose == 0, "Double-click did not reset semitone steps to the declared zero default");
                    Undo.PerformUndo(); Layout(); Need(q.transpose == 7, "Undo did not restore the authored semitone steps");
                    Undo.PerformRedo(); Layout(); Need(q.transpose == 0, "Redo did not restore the declared semitone default");
                });
            }
            finally
            {
                window.StopPreview(); window.Close(); Undo.ClearUndo(instrumentAsset); Undo.ClearUndo(songAsset);
                DestroyImmediate(window); DestroyImmediate(instrumentAsset); DestroyImmediate(songAsset);
            }
            report.Append("Pitch controls: ").Append(passed).Append(" passed, ").Append(failed).Append(" failed");
            return report.ToString();
        }
    }
}
