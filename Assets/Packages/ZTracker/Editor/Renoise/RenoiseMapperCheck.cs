using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Serial editor walkthrough. Uses native window pointer events; the OS picker is injected.
    /// Pauses on inspect/import/mapped/reimport for screenshots. Continue() advances; Finish() cleans up.</summary>
    public static class RenoiseMapperCheck
    {
        public static string Report { get; private set; } = "";
        public static RenoiseMapperWindow Window { get; private set; }
        static Queue<Action> steps;
        static string sourcePath, ownedFolder;
        static bool parentExisted, pause, runActive;
        static double next;
        static StringBuilder report;
        static XDocument xml;
        static RenoiseMapping row;
        static ZTrackerInstrument target;
        static string songBeforeAudition;
        static int passes;
        public static void Start(int walk = 1)
        {
            Finish(); report = new StringBuilder(); passes = 0; Report = "RUNNING";
            parentExisted = AssetDatabase.IsValidFolder("Assets/RenoiseImports");
            runActive = true;
            sourcePath = Path.Combine(Path.GetTempPath(), "RenoiseWalk" + walk + "_" + Guid.NewGuid().ToString("N").Substring(0,8) + ".xrns");
            xml = RenoiseSongCheck.CreateFixture("<Line index='0'><NoteColumns><NoteColumn><Note>C-4</Note><Instrument>00</Instrument></NoteColumn></NoteColumns></Line><Line index='8'><NoteColumns><NoteColumn><Note>G-4</Note></NoteColumn></NoteColumns></Line><Line index='12'><NoteColumns><NoteColumn><Note>OFF</Note></NoteColumn></NoteColumns></Line>");
            xml.Root.Element("Instruments").Elements().First().Add(new XElement("Name", "Helm lead"), XElement.Parse("<PluginGenerator><PluginDevice><PluginIdentifier>VST:Helm:check</PluginIdentifier><PluginType>VST</PluginType><PluginDisplayName>Helm</PluginDisplayName><ParameterChunk>state-A</ParameterChunk></PluginDevice></PluginGenerator>"));
            WriteSource();
            Window = RenoiseMapperWindow.OpenFor(null); Window.titleContent = new GUIContent("Renoise import check " + walk); Window.position = new Rect(120, 120, 1120, 680); Window.chooseSource = () => sourcePath;
            steps = new Queue<Action>();
            Step("Source button inspects without writing assets", () => { Click("renoise-source"); Need(Window.link == null, "Inspection created link"); });
            Step("inspect state", () => Eye("inspection"));
            Step("Import creates linked composition", () => { Click("renoise-apply"); Need(Window.link != null, Status()); ownedFolder = Window.link.ownedFolder; row = Window.link.mappings.First(m => m.source.Kind == "Instrument"); Need(row.instrument == null, "Plugin falsely converted"); });
            Step("choose source row", () => Click("renoise-select-" + row.source.Key));
            Step("new-target menu", () => Click("renoise-new-target"));
            Step("create Synth through menu", () => ClickText("Synth"));
            Step("assigned target", () => { row = Window.link.mappings.First(m => m.source.Kind == "Instrument"); target = row.instrument; Need(target != null && Window.link.song.model.instruments[0] == target, "New target not assigned"); Eye("mapped"); });
            Step("Dealt mark", () => { Click("renoise-dealt-" + row.source.Key); Need(row.dealtWith, "Mark did not toggle"); });
            Step("Undo review", () => { Click("renoise-undo"); row = Window.link.mappings.First(m => m.source.Kind == "Instrument"); Need(!row.dealtWith, "Undo failed"); });
            Step("Redo review", () => { Click("renoise-redo"); row = Window.link.mappings.First(m => m.source.Kind == "Instrument"); Need(row.dealtWith, "Redo failed"); });
            Step("Audition chosen range", () => { songBeforeAudition = JsonUtility.ToJson(Window.link.song); Click("renoise-audition"); Need(Status() == "Auditioning", Status()); });
            Step("audition renders PCM without changing composition", () => { var host = Resources.FindObjectsOfTypeAll<Laubrary.ZTracker.Engine.TrackerSapGenerator>().SingleOrDefault(h => h.gameObject.name == "Renoise mapper clean preview"); Need(host != null && host.RenderedFrames > 0 && host.RenderedEnergy > 0, "Clean audition has not rendered nonzero PCM"); Need(JsonUtility.ToJson(Window.link.song) == songBeforeAudition, "Audition changed the linked composition"); });
            Step("stop only mapper audition", () => Click("renoise-audition-stop"));
            Step("source revision inspected", () => { xml.Descendants("ParameterChunk").Single().Value = "state-B"; WriteSource(); Click("renoise-reimport"); Need(Window.link.sourceHash != Renoise.RenoiseArchive.Read(sourcePath).Hash, "Inspection wrote revision"); });
            Step("review source changes", () => { ClickText("Changes"); });
            Step("changes state", () => Eye("changes"));
            Step("apply reimport", () => { Click("renoise-apply"); Need(Window.link.mappings.Any(m => !m.removed && m.source.Kind == "Instrument" && m.instrument == target && m.dealtWith), "Choice/review lost"); });
            Step("save", () => Click("renoise-save"));
            Step("close and reopen recipe", () => { var song = Window.link.song; Window.Close(); Window = RenoiseMapperWindow.OpenFor(song); Window.titleContent = new GUIContent("Renoise import check reopen"); Window.position = new Rect(120,120,1120,680); });
            Step("persisted result", () => { Need(Window.link != null && Window.link.mappings.Any(m => !m.removed && m.instrument == target && m.dealtWith), "Reopen lost mapping"); Eye("reopened"); });
            Step("Issues tab", () => ClickText("Issues"));
            Step("issues state", () => Eye("issues"));
            next = EditorApplication.timeSinceStartup + .7; pause = false; EditorApplication.update += Tick;
        }
        static void Step(string label, Action action) => steps.Enqueue(() => { action(); passes++; report.AppendLine("PASS " + label); });
        static void Tick()
        {
            if (pause || EditorApplication.timeSinceStartup < next) return;
            try
            {
                if (steps.Count == 0) { Report = report + "RenoiseMapperCheck passed=" + passes + " failed=0"; Finish(); return; }
                steps.Dequeue()(); next = EditorApplication.timeSinceStartup + .6;
                if (!pause) Report = "RUNNING\n" + report;
            }
            catch (Exception ex) { Report = report + "FAIL " + ex + "\nRenoiseMapperCheck passed=" + passes + " failed=1"; EditorApplication.update -= Tick; }
        }
        static string Status() => Window.rootVisualElement.Q<Label>("renoise-status")?.text ?? "No status";
        static void Click(string name) => Click(Window.rootVisualElement.Q(name) ?? throw new Exception("Missing " + name));
        static void ClickText(string text)
        {
            VisualElement element = Window.rootVisualElement.panel.visualTree.Query<Button>().ToList().FirstOrDefault(b => b.text == text);
            if (element == null) element = Window.rootVisualElement.panel.visualTree.Query<Label>().ToList().FirstOrDefault(l => l.text == text && l.ClassListContains("zui-menu__label"))?.parent;
            Click(element ?? throw new Exception("Missing action " + text));
        }
        static void Click(VisualElement element)
        {
            var at = element.worldBound.center;
            Window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = at, clickCount = 1 });
            Window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = at, clickCount = 1 });
        }
        static void Eye(string stage)
        {
            var audit = ZuiAudit.Audit(Window, out int folded);
            Need(audit.Count == 0 && folded == 0, "UI audit " + string.Join("; ", audit));
            pause = true; Report = "EYE " + stage + "\n" + report;
        }
        public static void Continue() { pause = false; next = EditorApplication.timeSinceStartup + .4; }
        static void WriteSource()
        {
            using (var file = File.Create(sourcePath)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("Song.xml").Open(), new UTF8Encoding(false))) writer.Write(xml.ToString());
        }
        static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void Finish()
        {
            EditorApplication.update -= Tick;
            if (Window != null) { Window.Close(); Window = null; }
            if (!string.IsNullOrEmpty(ownedFolder) && ownedFolder.StartsWith("Assets/RenoiseImports/RenoiseWalk", StringComparison.Ordinal))
            {
                foreach (string guid in AssetDatabase.FindAssets("", new[] { ownedFolder }))
                { var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)); if (asset != null) Undo.ClearUndo(asset); }
                AssetDatabase.DeleteAsset(ownedFolder);
            }
            ownedFolder = null;
            if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath)) File.Delete(sourcePath); sourcePath = null;
            if (runActive && !parentExisted && AssetDatabase.IsValidFolder("Assets/RenoiseImports") && Directory.GetFileSystemEntries("Assets/RenoiseImports").Length == 0) AssetDatabase.DeleteAsset("Assets/RenoiseImports");
            runActive = false;
        }
    }
}
