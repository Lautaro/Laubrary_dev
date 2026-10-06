using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.Audio;
using Laubrary.Zounds;
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
        const string PolishTitle = "T0016 UI Proof";
        static ZTrackerModelWindow PolishWindow => Resources.FindObjectsOfTypeAll<ZTrackerModelWindow>().Single(w => w.titleContent.text == PolishTitle);

        // Separate calls allow the editor to lay out between setup, input and measurement.
        public static string OpenPolishProof(int width, int height, bool empty = false)
        {
            if (Resources.FindObjectsOfTypeAll<ZTrackerModelWindow>().Any(w => w.titleContent.text == PolishTitle)) throw new InvalidOperationException("Close the previous proof first.");
            var w = CreateInstance<ZTrackerModelWindow>();
            w.titleContent = new GUIContent(PolishTitle); w.position = new Rect(100, 100, width, height);
            if (!empty)
            {
                w.instrument = CreateInstance<ZTrackerInstrument>(); w.instrument.name = "T0016 transient instrument";
                w.instrument.schemaVersion = 1; w.instrument.model = NewInstrumentData();
                var d = w.instrument.model; d.name = "Proof sampler"; d.family = InstrumentFamily.Sampler; d.parameters.type = InstrumentType.Sample;
                d.sampler.samples.Add(new SampleData { id = "proof-sample", name = "Proof sample" });
                d.sampler.zones.Add(new Keyzone { id = "proof-zone", sample = 0, noteMin = 48, noteMax = 83 });
                w.song = CreateInstance<ZTrackerSong>(); w.song.name = "T0016 transient song"; w.song.schemaVersion = 1;
                w.song.model = TrackerEngineCheck.FixtureSong(w.instrument); w.song.model.tracks[0].name = "Track 1"; w.song.model.tracks[1].name = "Master";
                var nodes = w.song.model.tracks[0].devices.nodes;
                foreach (var type in new[] { ZoundEffectType.Gain, ZoundEffectType.Delay, ZoundEffectType.LowPass }) nodes.Add(new AudioEffectNodeData { uid = Id(), type = type });
                w.pane = 3; w.instrumentTab = 1;
            }
            w.Show(); w.CreateGUI(); return "proof=" + w.GetInstanceID() + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        }
        public static string ShowPolishProof(string surface, int width, int height)
        {
            var w = PolishWindow; w.position = new Rect(100, 100, width, height);
            if (surface == "zones") { w.pane = 3; w.instrumentTab = 1; }
            else if (surface == "devices") { w.pane = 1; w.mixerTab = 1; }
            else if (surface == "pattern") w.pane = 0;
            else throw new ArgumentException("Use zones, devices or pattern.");
            w.Rebuild(); ZuiAudit.ExpandAll(w); return w.position.ToString();
        }
        public static string ScrollPolishProof(string name)
        {
            var w = PolishWindow; var target = w.rootVisualElement.Q(name);
            if (target == null) throw new ArgumentException("Missing " + name);
            var scroll = target.GetFirstAncestorOfType<ScrollView>(); scroll?.ScrollTo(target); return target.worldBound.ToString();
        }
        public static string ClickPolishProof(string name)
        {
            var w = PolishWindow; var target = w.rootVisualElement.Q(name);
            if (target == null || target.worldBound.width <= 0) throw new ArgumentException("Missing or unlaid control " + name);
            var point = target.worldBound.center;
            var scroll = target.GetFirstAncestorOfType<ScrollView>();
            if (!w.rootVisualElement.worldBound.Contains(point) || scroll != null && !scroll.contentViewport.worldBound.Contains(point)) throw new InvalidOperationException("Scroll to " + name + " in a separate call before clicking.");
            w.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
            w.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
            return "Sent window mouse events to " + name;
        }
        public static string AuditPolishProof()
        {
            var w = PolishWindow; var issues = ZuiAudit.Audit(w, out int skipped);
            int folded = w.rootVisualElement.Query<ZuiBox>().ToList().Count(b => !b.IsOpen) + w.rootVisualElement.Query<ZuiSection>().ToList().Count(b => !b.IsOpen);
            var report = new StringBuilder("layout=" + w.position + " folded=" + folded + " rawHiddenSubtrees=" + skipped + " findings=" + issues.Count + "\n");
            foreach (var issue in issues) report.AppendLine(issue.ToString());
            var cards = w.rootVisualElement.Q("chain-device-cards");
            if (cards != null)
            {
                for (int c = 0; c < cards.childCount; c++) report.AppendLine("card " + c + " " + cards[c].worldBound);
                if (cards.childCount >= 2)
                {
                    bool beside = Mathf.Abs(cards[0].worldBound.y - cards[1].worldBound.y) < 1;
                    bool expected = cards.contentRect.width >= 648;
                    report.AppendLine("responsive-cards=" + (beside == expected ? "PASS" : "FAIL") + " available=" + cards.contentRect.width);
                    bool fit = cards.Children().All(c => c.worldBound.xMin >= cards.worldBound.xMin - 1 && c.worldBound.xMax <= cards.worldBound.xMax + 1);
                    report.AppendLine("card-horizontal-fit=" + (fit ? "PASS" : "FAIL"));
                }
            }
            foreach (string name in new[] { "zone-active", "zone-paired", "chain-param-0-0" })
            {
                var e = w.rootVisualElement.Q(name); if (e != null) report.AppendLine(name + " " + e.worldBound + " tooltip=" + e.tooltip);
            }
            return report.ToString();
        }
        public static string CheckPolishWorkflow()
        {
            var w = PolishWindow; var report = new StringBuilder(); int pass = 0, fail = 0;
            void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
            void Check(string name, Action action) { try { action(); report.AppendLine("PASS " + name); pass++; } catch (Exception e) { report.AppendLine("FAIL " + name + ": " + e.Message); fail++; } }
            void Click(string name) { var b = w.rootVisualElement.Q<Button>(name); Need(b != null, "Missing " + name); typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(b.clickable, new object[] { null, 0 }); }
            Check("Active polarity tooltip Undo and Redo", () =>
            {
                w.pane = 3; w.instrumentTab = 1; w.BuildPane(); var z = w.SelectedZone; Need(z != null, "No selected zone"); bool before = z.inactive;
                var toggle = w.rootVisualElement.Q<ZuiToggleButton>("zone-active"); Need(toggle.value == !before && toggle.text == "Active", "Incorrect polarity or label");
                Click("zone-active"); Need(w.SelectedZone.inactive == !before, "Toggle did not edit data"); toggle = w.rootVisualElement.Q<ZuiToggleButton>("zone-active"); Need(toggle.value == before && toggle.tooltip.StartsWith(before ? "Disable" : "Enable"), "Tooltip did not follow state");
                Undo.PerformUndo(); Need(w.SelectedZone.inactive == before && w.rootVisualElement.Q<ZuiToggleButton>("zone-active").value == !before, "Undo failed");
                Undo.PerformRedo(); Need(w.SelectedZone.inactive == !before, "Redo failed"); Undo.PerformUndo();
            });
            Check("Second sample has no redundant off box and retains real on group", () =>
            {
                Need(w.SelectedZone.blend == null, "Start with second sample off"); var toggle = w.rootVisualElement.Q("zone-paired"); Need(toggle.GetFirstAncestorOfType<ZuiBox>() == null, "Off toggle still has a box");
                Click("zone-paired"); Need(w.SelectedZone.blend != null && w.rootVisualElement.Q("zone-pcm-b") != null, "Enabled group missing controls");
                Need(w.rootVisualElement.Q("zone-paired").GetFirstAncestorOfType<ZuiBox>() != null, "Enabled group missing");
                Undo.PerformUndo(); Need(w.SelectedZone.blend == null, "Second sample Undo failed"); Undo.PerformRedo(); Need(w.SelectedZone.blend != null, "Second sample Redo failed"); Undo.PerformUndo();
            });
            Check("Mixer card scalar edit and reorder preserve complete Undo Redo", () =>
            {
                w.pane = 1; w.mixerTab = 1; w.BuildPane(); var nodes = w.SelectedTrack.devices.nodes; Need(nodes.Count >= 2, "Need two nodes");
                string first = nodes[0].uid, second = nodes[1].uid; var desc = Laubrary.Zounds.Dsp.ZoundEffectDescriptors.Get(nodes[0].type); float old = nodes[0].p != null && nodes[0].p.Length > 0 ? nodes[0].p[0] : desc.parameters[0].def;
                var dial = w.rootVisualElement.Q<ZuiMicroSlider>("chain-param-0-0"); Need(dial != null, "Gain slider absent");
                typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dial, new object[] { Laubrary.Audio.Editor.AudioChainEditor.DisplayValue(desc.parameters[0], .25f), true });
                Need(Math.Abs(w.SelectedTrack.devices.nodes[0].p[0] - .25f) < 1e-5, "Scalar edit failed"); Undo.PerformUndo(); nodes = w.SelectedTrack.devices.nodes; Need(Math.Abs((nodes[0].p == null || nodes[0].p.Length == 0 ? desc.parameters[0].def : nodes[0].p[0]) - old) < 1e-5, "Scalar Undo failed"); Undo.PerformRedo(); Need(Math.Abs(w.SelectedTrack.devices.nodes[0].p[0] - .25f) < 1e-5, "Scalar Redo failed"); Undo.PerformUndo();
                var target = w.rootVisualElement.Q("chain-node-1"); Need(target.panel != null, "No attached panel"); DragAndDrop.SetGenericData("audio.chain.node", 0);
                try { using (var evt = DragPerformEvent.GetPooled()) { evt.target = target; target.SendEvent(evt); } }
                finally { DragAndDrop.SetGenericData("audio.chain.node", null); }
                Need(w.SelectedTrack.devices.nodes[0].uid == second && w.SelectedTrack.devices.nodes[1].uid == first, "Attached drop did not reorder");
                Undo.PerformUndo(); Need(w.SelectedTrack.devices.nodes[0].uid == first, "Reorder Undo failed"); Undo.PerformRedo(); Need(w.SelectedTrack.devices.nodes[0].uid == second, "Reorder Redo failed"); Undo.PerformUndo();
                Need(w.rootVisualElement.Q("chain-device-cards")?.childCount == w.SelectedTrack.devices.nodes.Count, "Cards lost after rebuild");
            });
            report.AppendLine("TOTAL passed=" + pass + " failed=" + fail); return report.ToString();
        }
        public static string ClosePolishProof()
        {
            var w = PolishWindow; var s = w.song; var i = w.instrument;
            if (s != null && AssetDatabase.Contains(s) || i != null && AssetDatabase.Contains(i)) throw new InvalidOperationException("Persistent walk assets require explicit scoped cleanup.");
            w.StopPreview(); w.Close(); DestroyImmediate(w);
            if (s != null) { Undo.ClearUndo(s); DestroyImmediate(s); }
            if (i != null) { Undo.ClearUndo(i); DestroyImmediate(i); }
            return "Closed own window and transient assets.";
        }
    }
}
