using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.AssetKit.Editor;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Renoise;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>The linked import's mapper, reached only from the existing tracker toolbar.</summary>
    public sealed class RenoiseMapperWindow : ZuiWindow
    {
        [SerializeField] internal RenoiseImportLink link;
        [SerializeField] string selectedKey = "", sourcePath = "", destinationName = "Import";
        [SerializeField] int filter, sort, page, useIndex;
        [SerializeField] float rangeStart, rangeEnd;
        [SerializeField] bool showRemoved;
        RenoiseImportPlan plan;
        string error;
        VisualElement body, details;
        Label status;
        RenoiseAudition audition;
        readonly Dictionary<Object, Texture2D> thumbnails = new Dictionary<Object, Texture2D>();
        internal Func<string> chooseSource;

        public static RenoiseMapperWindow OpenFor(Object asset)
        {
            var window = CreateInstance<RenoiseMapperWindow>(); window.link = RenoiseImportService.Find(asset);
            window.titleContent = new GUIContent("Renoise import"); window.Show(); return window;
        }

        protected override void BuildUI(VisualElement root)
        {
            minSize = new Vector2(1080, 560);
            root.style.paddingLeft = root.style.paddingRight = 6;
            root.style.paddingTop = root.style.paddingBottom = 4;
            var bar = Row(Action("Source…", "Inspect an XRNS song or an exported XRNI instrument without changing any assets.", ChooseSource, "renoise-source"));
            if (link != null)
            {
                bar.Add(Action("Reimport", "Read the linked file and review source changes before applying. Existing target instruments are retained.", () => Inspect(link.sourcePath), "renoise-reimport"));
                bar.Add(Action("Save", "Save this mapping recipe and its linked song; no other project assets are saved.", Save, "renoise-save"));
                if (link.song != null) bar.Add(Action("Song", "Open the imported arrangement in ZTracker.", () => ZTrackerModelWindow.OpenFor(link.song), "renoise-song"));
            }
            if (plan != null)
            {
                if (link == null) bar.Add(Named(Z.TextInput(destinationName, "Name for a new owned folder under Assets/RenoiseImports.", v => destinationName = v, 150), "renoise-destination"));
                var apply = Action(link == null ? "Import" : "Apply revision", plan.blockedReason ?? "Create the inspected import, or replace its arrangement while retaining target sound design. This is one Undo step.", ApplyPlan, "renoise-apply");
                apply.SetEnabled(string.IsNullOrEmpty(plan.blockedReason)); bar.Add(apply);
                bar.Add(Action("Cancel", "Discard this inspected revision without writing assets.", () => { plan = null; error = null; Rebuild(); }, "renoise-cancel"));
            }
            bar.Add(Named(Z.IconButton("arrow-counter-clockwise", "Undo the last import or mapping change.", Undo.PerformUndo), "renoise-undo"));
            bar.Add(Named(Z.IconButton("arrow-clockwise", "Redo the last undone change.", Undo.PerformRedo), "renoise-redo"));
            bar.Add(Action("Guide", "Open the composer guide for the compatibility profile actually implemented.", () => Application.OpenURL(Path.GetFullPath("Assets/Packages/ZTracker/Documentation~/RENOISE_IMPORT.md")), "renoise-guide"));
            status = Text(error ?? (plan != null ? "Inspection" : link != null ? link.sourceName : "No source"), 190, error ?? (plan?.path ?? link?.sourcePath ?? "Choose Source to inspect a Renoise song or Redux instrument."));
            status.name = "renoise-status"; bar.Add(status); root.Add(bar);
            root.Add(Z.Segmented(page, new[] { "Mappings", "Issues", "Changes" }, "Choose the mapping list, grouped compatibility issues or source revision changes.", v => { page = v; Rebuild(); }));
            body = new VisualElement { name = "renoise-body" }; body.style.flexGrow = 1; body.style.minHeight = 0; root.Add(body);
            if (page == 0) BuildMappings(); else BuildReport();
        }

        List<RenoiseMapping> Mappings => plan?.mappings ?? link?.mappings ?? new List<RenoiseMapping>();
        RenoiseMapping Selected => Mappings.FirstOrDefault(m => m.source.Key == selectedKey);

        internal void Inspect(string path)
        {
            try
            {
                StopAudition(); error = null;
                var next = RenoiseImportService.Inspect(path, link);
                plan = next; sourcePath = path;
                destinationName = Path.GetFileNameWithoutExtension(path);
                if (!Mappings.Any(m => m.source.Key == selectedKey)) selectedKey = Ordered().FirstOrDefault()?.source.Key ?? "";
                ResetUse();
            }
            catch (Exception ex) { plan = null; error = ex.Message; }
            Rebuild();
        }

        void ChooseSource()
        {
            string path = chooseSource != null ? chooseSource() : EditorUtility.OpenFilePanel("Renoise song or Redux instrument", Path.GetDirectoryName(link?.sourcePath ?? sourcePath) ?? "", "xrns,xrni");
            if (!string.IsNullOrEmpty(path)) Inspect(path);
        }

        internal void ApplyPlan()
        {
            try
            {
                error = null;
                string safe = string.Join("_", (destinationName ?? "Import").Split(Path.GetInvalidFileNameChars())).Trim();
                if (safe.Length == 0) safe = "Import";
                string baseFolder = "Assets/RenoiseImports/" + safe, destination = baseFolder;
                for (int suffix = 2; Directory.Exists(destination) || File.Exists(destination); suffix++) destination = baseFolder + " " + suffix;
                link = RenoiseImportService.Apply(plan, destination, link); plan = null;
                ResetUse();
            }
            catch (Exception ex) { error = ex.Message; }
            Rebuild();
        }

        IEnumerable<RenoiseMapping> Ordered()
        {
            IEnumerable<RenoiseMapping> values = Mappings.Where(m => showRemoved || !m.removed);
            if (filter > 0) values = values.Where(m => (int)m.status == filter - 1);
            return sort == 1 ? values.OrderBy(m => m.source.Name, StringComparer.OrdinalIgnoreCase) : sort == 2 ? values.OrderByDescending(m => m.notes).ThenBy(m => m.source.Name) :
                values.OrderBy(m => m.removed).ThenBy(m => m.dealtWith).ThenBy(m => m.status).ThenBy(m => m.source.Kind).ThenBy(m => m.source.Name);
        }

        void BuildMappings()
        {
            var filters = Row(Z.Segmented(filter, new[] { "All", "Unmapped", "Approximate", "Mapped" }, "Filter by translation status; a dealt-with mark does not change compatibility.", v => { filter = v; Rebuild(); }),
                Z.Segmented(sort, new[] { "Exceptions", "Name", "Usage" }, "Sort unresolved decisions first, alphabetically, or by notes played in the sequence.", v => { sort = v; Rebuild(); }),
                Z.Toggle("History", "Include removed source entries and their retained choices.", showRemoved, v => { showRemoved = v; Rebuild(); }));
            body.Add(filters);
            body.Add(Row(Text("Source", 174), Text("Kind", 76), Text("Notes / rows / %", 110), Text("Pitch", 72), Text("Tracks", 118), Text("Status", 98), Text("Target", 165), Text("Dealt with", 86)));
            var scroll = new ScrollView { name = "renoise-mappings" }; scroll.style.flexGrow = 1; scroll.style.minHeight = 100; scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden; body.Add(scroll);
            foreach (var mapping in Ordered())
            {
                var m = mapping;
                var name = Action((m.removed ? "− " : "") + m.source.Name, m.source.Location + "\n" + m.source.PluginIdentifier + "\n" + m.source.Reason, () => { selectedKey = m.source.Key; ResetUse(); Rebuild(); }, "renoise-select-" + m.source.Key);
                name.style.width = 174; name.style.overflow = Overflow.Hidden; name.style.textOverflow = TextOverflow.Ellipsis; name.style.whiteSpace = WhiteSpace.NoWrap;
                if (m.source.Key == selectedKey) name.style.backgroundColor = new Color(.18f, .36f, .48f);
                string notes = m.notes + " / " + m.rows + " / " + (m.songShare * 100).ToString("0") + "%";
                if (m.notes == 0 && m.source.InstrumentIndex < 0 && (m.source.Kind == "Device" || m.source.Kind == "EffectChain")) notes = "—";
                var row = Row(name, Text(m.source.Kind, 76), Text(notes, 110, "Direct note occurrences and occupied rows in the sequence, including repeated patterns; share of all notes. Muted triggers are excluded. Indirect audio through group/send/master buses is not counted."),
                    Text(m.lowest < 0 ? "—" : Pitch(m.lowest) + "–" + Pitch(m.highest), 72), Text(string.Join(", ", m.tracks), 118), Text(m.status.ToString(), 98, m.detail));
                row.name = "renoise-row-" + m.source.Key;
                if (m.source.InstrumentIndex >= 0 && (m.source.Kind == "Instrument" || m.source.Kind == "Plugin"))
                {
                    if (plan == null && link != null && !m.removed) row.Add(InstrumentChip(m));
                    else row.Add(Text(m.instrument != null ? m.instrument.name : "—", 165));
                }
                else row.Add(Text(m.bypass ? "Dry" : m.chainOwner != null ? m.chainOwner.name : "—", 165, m.chainOwner != null ? "Replacement chain from " + m.chainOwner.name : m.detail));
                if (link != null && plan == null)
                {
                    var toggle = Named(Z.Toggle("Dealt", "Mark this mapping reviewed. Its compatibility status remains visible and this mark survives reimport.", m.dealtWith, v => Edit("Review Renoise mapping", () => m.dealtWith = v)), "renoise-dealt-" + m.source.Key);
                    toggle.style.width = 86; row.Add(toggle);
                }
                else row.Add(Text(m.dealtWith ? "Yes" : "—", 86));
                scroll.Add(row);
            }
            details = new VisualElement { name = "renoise-details" }; details.style.height = 170; details.style.flexShrink = 0; details.style.overflow = Overflow.Hidden; body.Add(details);
            BuildDetails();
        }

        void BuildDetails()
        {
            var m = Selected; if (m == null) return;
            details.Add(Row(Text(m.source.Name, 220, m.source.Location), Text(m.source.Suggestion, 600, m.source.Reason)));
            details.Add(Row(Text(m.matchedBy, 260, m.matchedBy + "\nStable matching key: " + m.source.Key), Text(m.source.PluginIdentifier.Length > 0 ? m.source.PluginIdentifier : m.source.Kind + (m.source.InstrumentIndex >= 0 ? " · instrument " + m.source.InstrumentIndex.ToString("X2") : ""), 640, m.source.Location + "\nState fingerprint: " + m.source.Fingerprint)));
            bool instrument = m.source.Kind == "Instrument" || m.source.Kind == "Plugin";
            if (link != null && plan == null && !m.removed)
            {
                if (instrument)
                {
                    var actions = Row(InstrumentChip(m));
                    Button create = null;
                    create = Action("New target…", "Create a ZTracker sound to replace this source instrument.", () =>
                    {
                        var menu = Z.Menu(create);
                        foreach (string type in new[] { "Synth", "FM", "Sampler", "Drum kit" }) { string picked = type; menu.Item(type, "Create and assign a " + type + " in this import's owned folder.", () => NewTarget(m, picked)); }
                        menu.Show();
                    }, "renoise-new-target"); actions.Add(create);
                    if (m.instrument != null) actions.Add(Action("Edit target", "Open this replacement's sound design in ZTracker.", () => ZTrackerModelWindow.OpenInstrument(m.instrument), "renoise-edit-target"));
                    actions.Add(Action("Clear", "Remove the replacement; notes and source payload remain.", () => { RenoiseImportService.SetInstrument(link, m, null); Rebuild(); }, "renoise-clear-target"));
                    details.Add(actions);
                    BuildAudition(m);
                }
                else
                {
                    var chain = ChainRow(m);
                    if (chain != null && chain.source.NestingDepth == 0)
                    {
                        var actions = Row(ChainPicker(chain, true), ChainPicker(chain, false), Action("Dry", "Replace the whole source chain with an empty chain. Instrument replacements make an owned copy of the target; source automation stays inactive.", () => SetChain(chain, null, "", true), "renoise-chain-dry"));
                        if (chain.source.TrackIndex >= 0) actions.Add(Action("Automatic", "Restore the restricted native conversion for this source chain.", () => RestoreChain(chain), "renoise-chain-auto"));
                        details.Add(actions);
                    }
                }
            }
        }

        VisualElement InstrumentChip(RenoiseMapping m)
        {
            var chip = new ZuiChip(m.instrument != null ? m.instrument.name : null, "Choose a ZTracker instrument from the shared asset browser, or drop one here.", empty: m.instrument == null) { name = "renoise-target-" + m.source.Key };
            chip.style.width = 165; chip.style.flexShrink = 0;
            if (m.instrument != null) { var thumbnail = LauAssetGridGUI.GetThumbnail(m.instrument, null, thumbnails); if (thumbnail != null) chip.Thumbnail = thumbnail; }
            chip.OnActivate = c => { var rect = c.worldBound; LauAssetBrowser.Show(rect, typeof(ZTrackerInstrument), picked => { RenoiseImportService.SetInstrument(link, m, picked as ZTrackerInstrument); Rebuild(); }, m.instrument); };
            chip.Accepts = o => o is ZTrackerInstrument;
            chip.OnDrop = o => { RenoiseImportService.SetInstrument(link, m, o as ZTrackerInstrument); Rebuild(); };
            return chip;
        }

        void NewTarget(RenoiseMapping m, string kind)
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            var target = ZTrackerModelWindow.CreateInstrumentAsset(m.source.Name + " " + kind, link.ownedFolder);
            if (kind == "Sampler") target.model.family = InstrumentFamily.Sampler;
            else if (kind == "Drum kit") target.model.family = InstrumentFamily.DrumKit;
            else if (kind == "FM") target.model.synthMode = SynthMode.FM;
            EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
            RenoiseImportService.SetInstrument(link, m, target); Undo.CollapseUndoOperations(group); Rebuild();
        }

        RenoiseMapping ChainRow(RenoiseMapping m) => m.source.Kind == "EffectChain" ? m : Mappings.FirstOrDefault(x => !x.removed && x.source.Key == m.source.ParentKey);
        Button ChainPicker(RenoiseMapping m, bool instrument)
        {
            Button button = null;
            button = Action(instrument ? "Instrument chain…" : "Song chain…", "Choose an existing ZTracker asset, then a chain; the entire source track chain is replaced.", () =>
            {
                LauAssetBrowser.Show(button.worldBound, instrument ? typeof(ZTrackerInstrument) : typeof(ZTrackerSong), selected =>
                {
                    var menu = Z.Menu(button);
                    if (selected is ZTrackerInstrument ins && ins.schemaVersion == 1)
                        for (int i = 0; i < ins.model.fxChains.Count; i++) { int index = i; menu.Item("Chain " + (i + 1), "Use the instrument's authored chain.", () => SetChain(m, ins, index.ToString(), false)); }
                    else if (selected is ZTrackerSong s && s.schemaVersion == 1)
                        foreach (var track in s.model.tracks) { var t = track; menu.Item(t.name, "Use this track's authored chain.", () => SetChain(m, s, t.id, false)); }
                    menu.Show();
                }, m.chainOwner);
            }, instrument ? "renoise-chain-instrument" : "renoise-chain-song");
            return button;
        }

        internal void SetChain(RenoiseMapping m, Object owner, string id, bool dry)
        {
            if (m.source.InstrumentIndex >= 0)
            {
                try { RenoiseInstrumentChainMapping.Apply(link, m, owner, id, dry); Rebuild(); }
                catch (Exception ex) { error = ex.Message; Rebuild(); }
                return;
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(link, "Map Renoise chain");
            if (link.song != null) Undo.RegisterCompleteObjectUndo(link.song, "Map Renoise chain");
            m.chainOwner = owner; m.chainId = id; m.bypass = dry; m.explicitTarget = true;
            if (link.song != null) { RenoiseImportService.ApplyChainChoices(link.song.model, new List<RenoiseMapping> { m }, link.issues); EditorUtility.SetDirty(link.song); }
            foreach (var child in link.mappings.Where(x => x.source.ParentKey == m.source.Key)) { child.status = RenoiseMappingStatus.Approximate; child.detail = "Covered by the explicit whole-chain replacement."; }
            EditorUtility.SetDirty(link); Undo.CollapseUndoOperations(group); Rebuild();
        }

        void RestoreChain(RenoiseMapping m)
        {
            try
            {
                var archived = RenoiseArchive.Read(link.archivePath); var converted = RenoiseSongConverter.Convert(archived, link.sourceIdentity);
                Edit("Restore Renoise chain", () =>
                {
                    Undo.RegisterCompleteObjectUndo(link.song, "Restore Renoise chain");
                    var source = converted.Song.tracks.First(t => t.id == m.targetTrackId); var destination = link.song.model.tracks.First(t => t.id == m.targetTrackId);
                    destination.devices = source.devices; destination.sourceDevices = source.sourceDevices; destination.sends = source.sends;
                    foreach (var pattern in link.song.model.patterns)
                    {
                        var restored = converted.Song.patterns.FirstOrDefault(p => p.id == pattern.id)?.tracks.FirstOrDefault(t => t.trackId == m.targetTrackId);
                        var current = pattern.tracks.FirstOrDefault(t => t.trackId == m.targetTrackId);
                        if (restored == null || current == null) continue;
                        foreach (var lane in restored.automation)
                        {
                            int at = current.automation.FindIndex(l => l.id == lane.id);
                            if (at >= 0 && current.automation[at].diagnostic == "Source chain replaced; rebind this lane explicitly.") current.automation[at] = ZTrackerMigration.Copy(lane);
                        }
                    }
                    m.chainOwner = null; m.chainId = ""; m.bypass = m.explicitTarget = false;
                    m.chainSnapshot = ZTrackerMigration.Copy(source.devices);
                    var devices = converted.Devices.Where(d => d.TrackIndex == m.source.TrackIndex).ToList();
                    m.status = devices.All(d => d.Mapped) ? RenoiseMappingStatus.Mapped : RenoiseMappingStatus.Unmapped;
                    m.detail = "Restricted source conversion restored.";
                    foreach (var child in link.mappings.Where(x => x.source.ParentKey == m.source.Key))
                    { child.status = devices.Any(d => d.DeviceIndex == child.source.DeviceIndex && d.Mapped) ? RenoiseMappingStatus.Mapped : RenoiseMappingStatus.Unmapped; child.detail = "Restricted source conversion restored."; }
                    EditorUtility.SetDirty(link.song);
                });
            }
            catch (Exception ex) { error = ex.Message; Rebuild(); }
        }

        void ResetUse()
        {
            useIndex = 0; var use = Selected?.uses.FirstOrDefault(); rangeStart = use?.firstRow ?? 0; rangeEnd = (use?.lastRow ?? 15) + 1;
        }

        void BuildAudition(RenoiseMapping m)
        {
            if (m.instrument == null) return;
            if (link.song == null || m.uses.Count == 0)
            {
                details.Add(Row(Action("Audition", "Play a short clean middle-C phrase through the replacement instrument.", () => StartAudition(m, null), "renoise-audition"), Action("Stop", "Stop only this mapper's preview.", StopAudition, "renoise-audition-stop")));
                return;
            }
            useIndex = Mathf.Clamp(useIndex, 0, m.uses.Count - 1); var use = m.uses[useIndex];
            int length = link.song.model.patterns[use.pattern].lineCount;
            Button picker = null;
            picker = Action(use.name, "Choose a sequence occurrence where this source instrument is used.", () =>
            {
                var menu = Z.Menu(picker);
                for (int i = 0; i < m.uses.Count; i++) { int at = i; var item = m.uses[i]; menu.Item(item.sequence.ToString("00") + " " + item.name, "Audition notes from this occurrence with the chosen target.", () => { useIndex = at; rangeStart = item.firstRow; rangeEnd = item.lastRow + 1; Rebuild(); }); }
                menu.Show();
            }, "renoise-use");
            picker.style.maxWidth = 270;
            var range = Z.MicroMinMax("Rows", rangeStart, rangeEnd, 0, length, "Zero-based start and exclusive end of the clean preview excerpt.", (a, b) => { rangeStart = Mathf.Floor(a); rangeEnd = Mathf.Max(rangeStart + 1, Mathf.Ceil(b)); }, 230, lowDefault: use.firstRow, highDefault: use.lastRow + 1, decimals: 0);
            details.Add(Row(picker, range, Action("Audition", "Play only this source's notes in the selected range through the replacement. The song's renderer keeps playing independently.", () => StartAudition(m, use), "renoise-audition"), Action("Stop", "Stop only this mapper's preview.", StopAudition, "renoise-audition-stop")));
        }

        void StartAudition(RenoiseMapping m, RenoiseUse use)
        {
            try { StopAudition(); audition = new RenoiseAudition(); audition.Play(link.song, m, use, (int)rangeStart, (int)rangeEnd); error = null; }
            catch (Exception ex) { StopAudition(); error = ex.Message; }
            if (status != null) { status.text = error ?? "Auditioning"; status.tooltip = error ?? "Clean mapper preview; the song's renderer is unchanged."; }
        }
        void StopAudition() { audition?.Dispose(); audition = null; }

        void BuildReport()
        {
            var scroll = new ScrollView { name = "renoise-report" }; scroll.style.flexGrow = 1; scroll.style.minHeight = 0; body.Add(scroll);
            if (page == 1)
            {
                foreach (var issue in RenoiseImportService.GroupIssues(plan?.issues ?? link?.issues ?? new List<RenoiseIssue>()))
                    scroll.Add(Row(Text(issue.Count.ToString(), 42), Text(issue.Code, 260), Text(issue.Message, 630, issue.Message + "\n" + issue.Location)));
            }
            else foreach (var change in plan?.changes ?? link?.changes ?? new List<string>()) scroll.Add(Text(change, 930));
        }

        void Edit(string label, Action change)
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(link, label); change(); EditorUtility.SetDirty(link); Undo.CollapseUndoOperations(group); Rebuild();
        }
        void Save() { if (link == null) return; AssetDatabase.SaveAssetIfDirty(link); if (link.song != null) AssetDatabase.SaveAssetIfDirty(link.song); }
        protected override void OnDisable() { StopAudition(); LauAssetGridGUI.ClearCache(thumbnails); base.OnDisable(); }
        static string Pitch(int p) => new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[p % 12] + (p / 12);
        static VisualElement Row(params VisualElement[] children) { var row = Z.Row(children); row.style.flexWrap = Wrap.NoWrap; row.style.flexShrink = 0; row.style.minHeight = 26; return row; }
        static Label Text(string text, float width, string tip = null) { var label = Z.Text(text ?? "", tooltip: tip ?? text ?? ""); label.style.width = width; label.style.flexShrink = 0; label.style.whiteSpace = WhiteSpace.NoWrap; label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis; return label; }
        static Button Action(string text, string tip, System.Action action, string name = null) { var button = Z.CompactButton(text, tip, action); button.name = name; button.style.flexGrow = 0; button.style.flexShrink = 0; button.style.alignSelf = Align.FlexStart; return button; }
        static T Named<T>(T control, string name) where T : VisualElement { control.name = name; return control; }
    }
}
