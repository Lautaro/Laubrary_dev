using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        [SerializeField] string drumId = "", drumSearch = "", drumTagFilter = "";
        [SerializeField] int drumSourceTab, drumSort, drumPage, drumFirstOctave = 3, drumLow = 36, drumHigh = 36;
        readonly List<DrumBrowserItem> drumItems = new List<DrumBrowserItem>();
        DrumBrowserItem drumPicked;
        VisualElement drumCards, drumKeys, drumDetails, drumPads, drumPageControls, drumTagChoices;
        Label drumBrowserStatus;
        Action redrawDrumMap, refreshDrumCategory;
        ZTrackerDrumPreview drumFilePreview;
        GameObject drumSourcePreview;
        PlaybackSnapshot drumSourceSnapshot;
        const string DrumDrag = "tracker.drum.source";
        const int DrumPageSize = 100;
        static readonly string[] DrumCategories = { "All", "Kick", "Snare", "Hats", "Percussion", "Shaker", "Misc", "FX" };
        sealed class DrumBrowserItem
        {
            public string name, tag, path;
            public AudioClip clip;
            public ZTrackerInstrument synth;
        }
        DrumMapping DrumSelection => Instrument?.drumKit?.mappings.Find(m => m.id == drumId);
        static string DrumNote(int n) => noteNames[Mathf.Clamp(n, 0, 119) % 12] + (n / 12);

        void CreateDrumKit()
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            CreateInstrument();
            InstrumentEdit("drum kit", () => { Instrument.family = InstrumentFamily.DrumKit; Instrument.name = "Drum kit"; }, true);
            Undo.CollapseUndoOperations(group);
            Undo.SetCurrentGroupName("Tracker: create drum kit");
        }
        void DrumEdit(string label, Action edit, bool rebuild = true) => InstrumentEdit(label, edit, rebuild);
        void BuildDrumKit(VisualElement root)
        {
            // Undo restores the authored mapping, while the selected id remains view state.
            // Reconcile its highlight before building the retained keyboard again.
            var selection = DrumSelection;
            if (selection != null) { drumLow = selection.noteMin; drumHigh = selection.noteMax; }
            refreshDrumCategory = null;
            stage.focusable = true;
            BuildDrumBrowser(controls);
            root.Add(Flow(
                Named(Z.ScrubNumber("Octave", drumFirstOctave, 0, 8, "Show two octaves of keys. Z–M plays the lower octave; Q–U plays the next.", v => { ReleaseInstrumentKeys(); drumFirstOctave = v; redrawDrumMap?.Invoke(); }, 100, defaultValue: 3), "drum-octave"),
                Button("Assign", "Assign the selected browser sound to the selected key range.", () => AssignDrumItem(drumPicked, drumLow, drumHigh), "drum-assign"),
                Button("File…", "Choose an external audio file and assign it to the selected keys. A copy is imported into this project.", ChooseDrumFile, "drum-file"),
                Named(Z.IconButton("trash", "Remove the selected key assignment; Undo restores it.", () => { var m = DrumSelection; if (m != null) DrumEdit("remove drum", () => Instrument.drumKit.mappings.Remove(m)); }), "drum-remove"),InstrumentKeyboardStatus()));
            drumKeys = new VisualElement { name = "drum-key-map", focusable = true, tooltip = "Click a key to play. Drag across empty keys to select a range, then drop a sound or press Assign. Drag a coloured assignment to move it; drag either end to resize it. Z–M / Q–U plays these two octaves." };
            drumKeys.style.height = 144; drumKeys.style.flexShrink = 0; drumKeys.style.backgroundColor = new Color(.075f, .09f, .12f);
            root.Add(drumKeys);
            BuildDrumKeyMap(drumKeys);
            var scroll = AuthoringScroll(root, "drum-mappings");
            drumDetails = new VisualElement(); scroll.Add(drumDetails); RefreshDrumDetails();
        }
        void BuildDrumBrowser(VisualElement root)
        {
            root.Add(Named(Z.Segmented(drumSourceTab, new[] { "Folder", "Project", "Synth" }, "Choose external audio, project clips, or synth instruments.", v => { drumSourceTab = v; drumPicked = null; ReloadDrumBrowser(); }), "drum-source-tabs"));
            root.Add(Flow(Button("Folder…", "Set the drum library folder. Each file's immediate parent folder supplies its tag.", ChooseDrumFolder, "drum-folder"), Button("↻", "Rescan the sound browser.", ReloadDrumBrowser, "drum-rescan")));
            root.Add(Named(Z.TextInput(drumSearch, "Find sounds by name or immediate parent folder, ignoring spaces, hyphens, underscores and case. Drum aliases match the same category.", v => { drumSearch = v; drumPage = 0; FilterDrumBrowser(); }, 170), "drum-search"));
            drumTagChoices=new VisualElement{name="drum-tag-filter"};root.Add(drumTagChoices);
            drumBrowserStatus = Z.Text("", tooltip: "Sound browser results."); drumBrowserStatus.style.height = 20; drumBrowserStatus.style.overflow = Overflow.Hidden;
            root.Add(Flow(Z.Segmented(drumSort, new[] { "Name", "Tag" }, "Sort sounds alphabetically or by sound category.", v => { drumSort = v; drumPage = 0; FilterDrumBrowser(); }), drumBrowserStatus));
            drumPageControls = Flow(); drumPageControls.name = "drum-page-controls"; root.Add(drumPageControls);
            drumCards = Flow(); drumCards.name = "drum-browser"; root.Add(drumCards); ReloadDrumBrowser();
        }
        void ReloadDrumBrowser()
        {
            drumItems.Clear(); string warning = "";
            if (drumSourceTab == 0)
            {
                var scan = ZTrackerDrumLibrary.Scan();
                foreach (var f in scan.files) drumItems.Add(new DrumBrowserItem { name = f.name, tag = f.parentFolderTag, path = f.fullPath });
                warning = string.Join("\n", scan.errors.Select(e => e.path + ": " + e.message));
            }
            else if (drumSourceTab == 1)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid); var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null) drumItems.Add(new DrumBrowserItem { name = clip.name, tag = Path.GetFileName(Path.GetDirectoryName(path)), clip = clip, path = path });
                }
            }
            else
            {
                foreach (var guid in AssetDatabase.FindAssets("t:ZTrackerInstrument", new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid); var synth = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(path);
                    if (ReadInstrument(synth)?.family == InstrumentFamily.Synth) drumItems.Add(new DrumBrowserItem { name = string.IsNullOrWhiteSpace(InstrumentTitle(synth)) ? synth.name : InstrumentTitle(synth), tag = "Misc", synth = synth, path = path });
                }
            }
            if (drumBrowserStatus != null) drumBrowserStatus.userData = warning;
            if(drumTagChoices!=null)
            {
                var tags=new[]{"All"}.Concat(drumItems.Select(i=>DrumTags.Canonical(i.tag)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t=>t,StringComparer.OrdinalIgnoreCase)).ToArray();
                int picked=drumTagFilter.Length==0?0:Array.FindIndex(tags,t=>DrumTags.Normalize(t)==DrumTags.Normalize(DrumTags.Canonical(drumTagFilter)));
                if(picked<0){drumTagFilter="";picked=0;}
                drumTagChoices.Clear();drumTagChoices.Add(Z.MiniRadio(picked,tags,"Filter by the sound's immediate parent folder. Recognised aliases share a category; other folder names keep their own tag.",v=>{drumTagFilter=v==0?"":tags[v];drumPage=0;FilterDrumBrowser();},wrap:true));
            }
            FilterDrumBrowser();
        }
        void FilterDrumBrowser()
        {
            if (drumCards == null) return; drumCards.Clear();
            string search = ZTrackerDrumLibrary.NormalizeTag(drumSearch);
            string canonicalSearch = DrumTags.Normalize(DrumTags.Canonical(drumSearch));
            bool AliasMatch(DrumBrowserItem item) => search.Length > 0 && DrumCategories.Skip(1).Any(c => canonicalSearch == DrumTags.Normalize(c)) && DrumTags.Category(drumSearch) == DrumTags.Category(item.tag);
            var matches = drumItems.Where(i => (drumTagFilter.Length == 0 || DrumTags.Normalize(DrumTags.Canonical(i.tag)) == DrumTags.Normalize(DrumTags.Canonical(drumTagFilter))) && (search.Length == 0 || ZTrackerDrumLibrary.NormalizeTag(i.name).Contains(search) || ZTrackerDrumLibrary.NormalizeTag(i.tag).Contains(search) || AliasMatch(i)));
            var sorted = drumSort == 0 ? matches.OrderBy(i => i.name, StringComparer.OrdinalIgnoreCase) : matches.OrderBy(i => DrumTags.Category(i.tag)).ThenBy(i => i.name, StringComparer.OrdinalIgnoreCase);
            var allMatches = sorted.ToList();
            int total = allMatches.Count;
            int pageCount = Math.Max(1, (total + DrumPageSize - 1) / DrumPageSize);
            drumPage = Mathf.Clamp(drumPage, 0, pageCount - 1);
            int first = drumPage * DrumPageSize;
            int end = Math.Min(first + DrumPageSize, total);
            drumPageControls?.Clear();
            if (drumPageControls != null && pageCount > 1)
            {
                var previous=Button("Previous", "Show the previous 100 sounds.", () => { drumPage--; FilterDrumBrowser(); }, "drum-page-previous");previous.SetEnabled(drumPage>0);drumPageControls.Add(previous);
                drumPageControls.Add(Z.Text($"Page {drumPage + 1} of {pageCount}", tooltip: "Every matching sound is available across these pages."));
                var next=Button("Next", "Show the next 100 sounds.", () => { drumPage++; FilterDrumBrowser(); }, "drum-page-next");next.SetEnabled(drumPage<pageCount-1);drumPageControls.Add(next);
            }
            int count = 0;
            foreach (var item in allMatches.Skip(first).Take(DrumPageSize))
            {
                count++; var card = Button(item.name, item.tag + "\n" + item.path + "\nClick to hear; drag to a key or range.", () => { drumPicked = item; PreviewDrumSource(item); foreach (var b in drumCards.Children()) b.EnableInClassList("tracker-picked", b.userData == item); }, "drum-source-" + (first + count));
                card.userData = item; card.EnableInClassList("tracker-picked", drumPicked != null && drumPicked.path == item.path); card.style.flexDirection = FlexDirection.Row;
                card.AddToClassList("tracker-drum-source");card.AddToClassList("zui-button-link");
                card.style.width = 155; card.style.overflow = Overflow.Hidden; card.style.textOverflow = TextOverflow.Ellipsis;
                SetDrumButtonContent(card, item.tag);
                Vector2 down = default; bool dragging = false;
                card.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; down = e.position; dragging = false; drumPicked = item; card.CapturePointer(e.pointerId); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
                card.RegisterCallback<PointerMoveEvent>(e => { if (!card.HasPointerCapture(e.pointerId)) return; dragging |= Vector2.Distance(down, e.position) >= 5; if (dragging) card.EnableInClassList("tracker-picked", true); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
                card.RegisterCallback<PointerUpEvent>(e => {
                    if (!card.HasPointerCapture(e.pointerId)) return;
                    card.ReleasePointer(e.pointerId); e.StopImmediatePropagation();
                    if (dragging && drumKeys != null && drumKeys.worldBound.Contains(e.position)) {
                        int at = Mathf.Clamp(drumFirstOctave * 12 + Mathf.FloorToInt(drumKeys.WorldToLocal(e.position).x / Math.Max(1, drumKeys.contentRect.width) * 24), 0, 119);
                        AssignDrumItem(item, at >= drumLow && at <= drumHigh ? drumLow : at, at >= drumLow && at <= drumHigh ? drumHigh : at);
                    } else if (!dragging) { PreviewDrumSource(item); foreach (var b in drumCards.Children()) b.EnableInClassList("tracker-picked", b.userData == item); }
                }, TrickleDown.TrickleDown);
                drumCards.Add(card);
            }
            string warning = drumBrowserStatus.userData as string;
            string range = total == 0 ? "0" : $"{first + 1}-{end} of {total}";
            drumBrowserStatus.text = (string.IsNullOrEmpty(warning) ? "" : "⚠ ") + range;
            drumBrowserStatus.tooltip = (string.IsNullOrEmpty(warning) ? "" : warning + "\n\n") + $"Showing {range} matching sounds; {drumItems.Count} loaded from this source. Click a sound to hear it; drag it to a key. " + ZTrackerDrumLibrary.GetRoot();
        }
        void PreviewDrumSource(DrumBrowserItem item)
        {
            StopDrumSourcePreview();
            if (item.synth != null)
            {
                try
                {
                    drumSourceSnapshot = PlaybackSnapshot.Create(null, item.synth, -1);
                    var prepared = TrackerPreparedSong.Prepare(drumSourceSnapshot.Data, AudioSettings.outputSampleRate);
                    drumSourcePreview = new GameObject("Drum source preview") { hideFlags = HideFlags.HideAndDontSave };
                    var generator = drumSourcePreview.AddComponent<TrackerSapGenerator>(); generator.Configure(prepared, false);
                    var audio = drumSourcePreview.GetComponent<AudioSource>(); audio.spatialBlend = 0; audio.generator = generator; audio.Play(); generator.SendCommand(TrackerCommand.Audition(0, DrumSelection?.baseNote ?? 60, 110, 0));
                    var owned = drumSourcePreview;
                    rootVisualElement.schedule.Execute(() => { if (drumSourcePreview == owned) StopDrumSourcePreview(); }).StartingIn(700);
                }
                catch (Exception ex) { StopDrumSourcePreview(); DrumError(ex.Message); }
            }
            else if (item.clip != null) PlayDrumClip(item.clip);
            else
            {
                if (drumFilePreview == null) drumFilePreview = new ZTrackerDrumPreview();
                drumFilePreview.Preview(item.path, PlayDrumClip, DrumError);
            }
        }
        void PlayDrumClip(AudioClip clip)
        {
            if (clip == null) return;
            if (drumSourcePreview != null) DestroyImmediate(drumSourcePreview);
            drumSourcePreview = new GameObject("Drum clip preview") { hideFlags = HideFlags.HideAndDontSave };
            var source = drumSourcePreview.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0; source.clip = clip; source.Play();
            var owned = drumSourcePreview;
            rootVisualElement.schedule.Execute(() => { if (drumSourcePreview == owned) StopDrumSourcePreview(); }).StartingIn(Mathf.CeilToInt(clip.length * 1000) + 100);
        }
        void StopDrumSourcePreview()
        {
            drumFilePreview?.Cancel();
            if (drumSourcePreview != null) DestroyImmediate(drumSourcePreview);
            drumSourcePreview = null; drumSourceSnapshot?.Dispose(); drumSourceSnapshot = null;
        }
        void DisposeDrumPreview() { StopDrumSourcePreview(); drumFilePreview?.Dispose(); drumFilePreview = null; }
        void DrumError(string message) { if (drumBrowserStatus != null) { drumBrowserStatus.text = "⚠ Sound unavailable"; drumBrowserStatus.tooltip = message; } Debug.LogWarning("ZTracker drum kit: " + message); }
        void ChooseDrumFile()
        {
            EditorApplication.delayCall += () => {
                if (this == null) return;
                var path = EditorUtility.OpenFilePanelWithFilters("Drum sample", ZTrackerDrumLibrary.GetRoot(), new[] { "Audio", "wav,ogg,mp3,aiff,aif" });
                if (path.Length > 0) AssignDrumItem(new DrumBrowserItem { path = path, name = Path.GetFileNameWithoutExtension(path), tag = Path.GetFileName(Path.GetDirectoryName(path)) }, drumLow, drumHigh);
            };
        }
        void ChooseDrumFolder()
        {
            // As in Zounds, let UI Toolkit finish the click and release capture before opening a native dialog.
            EditorApplication.delayCall += () => {
                if (this == null) return;
                var path = EditorUtility.OpenFolderPanel("Drum library", ZTrackerDrumLibrary.GetRoot(), "");
                if (string.IsNullOrEmpty(path)) return;
                ZTrackerDrumLibrary.SetRoot(path); drumSourceTab = 0; drumPicked = null; BuildPane();
            };
        }
        void AssignDrumItem(DrumBrowserItem item, int low, int high)
        {
            if (item == null || Instrument?.family != InstrumentFamily.DrumKit) return;
            AudioClip clip = item.clip; string url = "";
            if (item.synth == null)
            {
                if (clip == null)
                {
                    var imported = ZTrackerDrumLibrary.Import(new ZTrackerDrumLibrary.DrumFile { fullPath = item.path, name = item.name, parentFolderTag = item.tag });
                    if (!imported.Succeeded) { DrumError(imported.error); return; } clip = imported.clip; url = imported.sourceUrl;
                }
                else { var imported = ZTrackerDrumLibrary.ImportProjectClip(clip); if (!imported.Succeeded) { DrumError(imported.error); return; } clip = imported.clip; url = imported.sourceUrl; }
            }
            var mapping = new DrumMapping { id = Id(), name = item.name, tag = item.tag, noteMin = low, noteMax = high, baseNote = item.synth != null ? 60 : low, keyTracking = item.synth == null && low != high, pcm = clip, synth = item.synth, sourceUrl = url ?? "" };
            DrumEdit("assign drum", () => { CarveDrumRange(low, high, null); Instrument.drumKit.mappings.Add(mapping); drumId = mapping.id; drumLow = low; drumHigh = high; });
        }
        // Assigning a range replaces only those keys; unaffected parts of a mapping survive.
        void CarveDrumRange(int low, int high, DrumMapping except)
        {
            var list = Instrument.drumKit.mappings;
            foreach (var m in list.ToArray())
            {
                if (m == except || m.noteMax < low || m.noteMin > high) continue;
                int end = m.noteMax;
                if (m.noteMin < low) { m.noteMax = low - 1; if (end > high) { var right = Clone(m); right.id = Id(); right.noteMin = high + 1; right.noteMax = end; list.Add(right); } }
                else if (end > high) m.noteMin = high + 1;
                else list.Remove(m);
            }
        }
        void BuildDrumKeyMap(VisualElement canvas)
        {
            int first = drumFirstOctave * 12;
            int Key(float x) => Mathf.Clamp(first + Mathf.FloorToInt(x / Math.Max(1, canvas.contentRect.width) * 24), first, first + 23);
            Rect MappingRect(DrumMapping m) => new Rect((Math.Max(first, m.noteMin) - first) * canvas.contentRect.width / 24, 8, (Math.Min(first + 23, m.noteMax) - Math.Max(first, m.noteMin) + 1) * canvas.contentRect.width / 24, 48);
            void PaintLabels()
            {
                first = drumFirstOctave * 12;
                canvas.Clear(); float w = canvas.contentRect.width / 24;
                for (int i = 0; i < 24; i++) { var l = PlotLabel(canvas, DrumNote(first + i), i * w + 1, 111, w - 2); l.style.fontSize = 10; l.style.color = Color.white; }
                foreach (var m in Instrument.drumKit.mappings.Where(m => m.noteMax >= first && m.noteMin <= first + 23))
                {
                    var r = MappingRect(m); var icon = ZTrackerDrumIcons.Create(m.tag, 18); icon.pickingMode = PickingMode.Ignore; Place(icon, r.x + 2, r.y + 6, 18); canvas.Add(icon);
                    if (r.width > 65) { var label = PlotLabel(canvas, m.name, r.x + 22, r.y + 8, r.width - 24); label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis; label.style.whiteSpace = WhiteSpace.NoWrap; }
                }
            }
            redrawDrumMap = () => { PaintLabels(); canvas.MarkDirtyRepaint(); };
            int hoveredKey = first;
            canvas.RegisterCallback<PointerMoveEvent>(e => hoveredKey = Key(canvas.WorldToLocal(e.position).x));
            canvas.RegisterCallback<TooltipEvent>(e =>
            {
                int note = hoveredKey;
                var mapping = Instrument.drumKit.Find(note);
                e.tooltip = DrumNote(note) + (mapping == null ? " — Unassigned" : " — " + mapping.name + " (" + DrumTags.Canonical(mapping.tag) + ")") + "\n" + canvas.tooltip;
                e.rect = canvas.worldBound;
                e.StopPropagation();
            });
            canvas.RegisterCallback<GeometryChangedEvent>(_ => PaintLabels());
            canvas.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D; float w = canvas.contentRect.width / 24;
                void Box(Rect r, Color fill, Color stroke) { p.fillColor = fill; p.strokeColor = stroke; p.lineWidth = 1; p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.y)); p.LineTo(r.max); p.LineTo(new Vector2(r.x, r.yMax)); p.ClosePath(); p.Fill(); p.Stroke(); }
                for (int i = 0; i < 24; i++) { int n = first + i; bool black = new[] { 1, 3, 6, 8, 10 }.Contains(n % 12); Box(new Rect(i * w, 66, w - 1, 70), n >= drumLow && n <= drumHigh ? new Color(.2f, .5f, .7f) : black ? new Color(.12f, .14f, .18f) : new Color(.32f, .35f, .4f), Color.gray); }
                foreach (var m in Instrument.drumKit.mappings.Where(m => m.noteMax >= first && m.noteMin <= first + 23)) Box(MappingRect(m), new Color(.15f, .35f, .45f), m.id == drumId ? Color.yellow : Color.cyan);
            };
            DrumMapping moving = null; int origin = -1, edge = 0, oldLow = 0, oldHigh = 0, sounding = -1; bool selecting = false;
            // A clicked key plays like a typed one and is released when the pointer lifts.
            void Lift() { if (sounding >= 0) midiRouter.ComputerKeyUp(sounding); sounding = -1; }
            canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return; canvas.Focus(); origin = Key(e.localPosition.x);
                moving = e.localPosition.y < 60 ? Instrument.drumKit.mappings.Find(m => m.noteMin <= origin && m.noteMax >= origin) : null;
                if (moving != null) { drumId = moving.id; drumLow = oldLow = moving.noteMin; drumHigh = oldHigh = moving.noteMax; var r = MappingRect(moving); edge = Math.Abs(e.localPosition.x - r.x) < 6 ? -1 : Math.Abs(e.localPosition.x - r.xMax) < 6 ? 1 : 0; BeginInstrumentGesture("drum range"); }
                else { selecting = true; drumLow = drumHigh = origin; var hit = Instrument.drumKit.mappings.Find(m => m.noteMin <= origin && m.noteMax >= origin); drumId = hit?.id ?? ""; Lift(); PlayPreviewKey(origin); sounding = origin; }
                canvas.CapturePointer(e.pointerId); canvas.MarkDirtyRepaint(); RefreshDrumDetails(); e.StopPropagation();
            });
            canvas.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!canvas.HasPointerCapture(e.pointerId)) return; int at = Key(e.localPosition.x);
                if (moving != null) { int delta = Mathf.Clamp(at - origin, -oldLow, 119 - oldHigh); moving.noteMin = edge == 1 ? oldLow : edge == -1 ? Math.Min(at, oldHigh) : oldLow + delta; moving.noteMax = edge == -1 ? oldHigh : edge == 1 ? Math.Max(at, oldLow) : oldHigh + delta; }
                else if (selecting) { drumLow = Math.Min(origin, at); drumHigh = Math.Max(origin, at); }
                PaintLabels(); canvas.MarkDirtyRepaint();
            });
            canvas.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!canvas.HasPointerCapture(e.pointerId)) return;
                if (moving != null) { CarveDrumRange(moving.noteMin, moving.noteMax, moving); drumLow = moving.noteMin; drumHigh = moving.noteMax; moving = null; EndInstrumentGesture(); }
                Lift(); selecting = false; canvas.ReleasePointer(e.pointerId); PaintLabels(); canvas.MarkDirtyRepaint(); RefreshDrumDetails();
            });
            canvas.RegisterCallback<PointerCaptureOutEvent>(_ => { if (moving != null) { moving.noteMin = oldLow; moving.noteMax = oldHigh; moving = null; EndInstrumentGesture(); } Lift(); selecting = false; });
            canvas.RegisterCallback<DragUpdatedEvent>(e => { if (DrumDropItem() != null) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation(); } });
            canvas.RegisterCallback<DragPerformEvent>(e => { var item = DrumDropItem(); if (item == null) return; int at = Key(canvas.WorldToLocal(e.mousePosition).x); int low = at >= drumLow && at <= drumHigh ? drumLow : at, high = at >= drumLow && at <= drumHigh ? drumHigh : at; DragAndDrop.AcceptDrag(); DragAndDrop.SetGenericData(DrumDrag, null); AssignDrumItem(item, low, high); e.StopPropagation(); });
        }
        DrumBrowserItem DrumDropItem()
        {
            if (DragAndDrop.GetGenericData(DrumDrag) is DrumBrowserItem item) return item;
            var clip = DragAndDrop.objectReferences.OfType<AudioClip>().FirstOrDefault();
            if (clip != null) { string path = AssetDatabase.GetAssetPath(clip); return new DrumBrowserItem { clip = clip, name = clip.name, path = path, tag = Path.GetFileName(Path.GetDirectoryName(path)) }; }
            var synth = DragAndDrop.objectReferences.OfType<ZTrackerInstrument>().FirstOrDefault();
            if (synth != null && ReadInstrument(synth)?.family == InstrumentFamily.Synth) return new DrumBrowserItem { synth = synth, name = InstrumentTitle(synth), tag = "Misc" };
            var external = DragAndDrop.paths.FirstOrDefault(p => new[] { ".wav", ".ogg", ".mp3", ".aiff", ".aif" }.Contains(Path.GetExtension(p).ToLowerInvariant()));
            return external == null ? null : new DrumBrowserItem { path = external, name = Path.GetFileNameWithoutExtension(external), tag = Path.GetFileName(Path.GetDirectoryName(external)) };
        }
        void RefreshDrumPresentation()
        {
            redrawDrumMap?.Invoke();
            RefreshDrumPads();
            refreshDrumCategory?.Invoke();
        }
        void RefreshDrumPads()
        {
            if (drumPads == null) return;
            drumPads.Clear();
            foreach (var m in Instrument.drumKit.mappings.OrderBy(m => m.noteMin))
            {
                var pad = Button(m.name + "  " + DrumNote(m.noteMin) + (m.noteMin == m.noteMax ? "" : "–" + DrumNote(m.noteMax)), m.tag + "\nClick to select and play this assignment.", () => { drumId = m.id; drumLow = m.noteMin; drumHigh = m.noteMax; drumFirstOctave = Mathf.Clamp(m.noteMin / 12, 0, 8); TapPreviewKey(m.noteMin); BuildPane(); }, "drum-pad-" + m.id);
                pad.style.width = 235; SetDrumButtonContent(pad, m.tag); drumPads.Add(pad);
            }
        }
        static void SetDrumButtonContent(Button button, string tag)
        {
            // Button.text paints across its whole content area, including icon children.
            // A separate flex label reserves the icon's width and truncates long names.
            var label = new Label(button.text) { pickingMode = PickingMode.Ignore };
            button.text = "";
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.overflow = Overflow.Hidden;
            label.style.flexGrow = 1; label.style.flexShrink = 1; label.style.minWidth = 0;
            label.style.marginLeft = 5; label.style.marginRight = 0;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis;
            button.Add(ZTrackerDrumIcons.Create(tag, 16)); button.Add(label);
        }
        void RefreshDrumDetails()
        {
            if (drumDetails == null) return; drumDetails.Clear(); refreshDrumCategory = null;
            drumPads = Flow(); drumPads.name = "drum-pads"; drumDetails.Add(drumPads); RefreshDrumPads();
            var selected = DrumSelection; if (selected == null) return;
            var box = Z.BoxKeyed("Sound", "The selected assignment's source, type and key range.", "tracker.drum.sound"); drumDetails.Add(box);
            box.Add(Flow(Z.TextInput(selected.name, "Rename this key assignment.", v => { DrumEdit("drum name", () => selected.name = v, false); RefreshDrumPresentation(); }, 170),
                Z.MicroMinMax("Keys", selected.noteMin, selected.noteMax, 0, 119, "The inclusive key range. Keys taken from another assignment replace only the overlapping part.", (a,b) => { DrumEdit("drum range", () => { drumLow = selected.noteMin = Mathf.RoundToInt(a); drumHigh = selected.noteMax = Mathf.RoundToInt(b); CarveDrumRange(selected.noteMin, selected.noteMax, selected); }, false); RefreshDrumPresentation(); }, 230, lowDefault: 60, highDefault: 60, decimals: 0)));
            box.Add(Z.Field("Tag", "Sound category, initially the source file's parent folder.", Z.TextInput(selected.tag, "Type a custom tag or choose a standard category below. Aliases share an icon.", v => { DrumEdit("drum tag", () => selected.tag = v, false); RefreshDrumPresentation(); }, 170)));
            var categoryHost = new VisualElement { name = "drum-category-options" }; box.Add(categoryHost);
            int shownCategory = -1;
            refreshDrumCategory = () => {
                int category = (int)DrumTags.Category(selected.tag);
                if (category == shownCategory) return;
                shownCategory = category; categoryHost.Clear();
                categoryHost.Add(Z.MiniRadio(category, DrumCategories.Skip(1).ToArray(), "Assign a standard drum category.", v => DrumEdit("drum category", () => selected.tag = DrumCategories[v + 1]), wrap: true));
            };
            refreshDrumCategory();
            box.Add(Flow(Z.Field("Sample", "Replace the source with a project AudioClip.", Named(Z.Object(selected.pcm, "Choose an AudioClip; the synth source is cleared.", v => { if (v != null) AssignDrumItem(new DrumBrowserItem { clip = v, name = v.name, tag = selected.tag }, selected.noteMin, selected.noteMax); }, 200), "drum-pcm")),
                Z.Field("Synth", "Replace the source with a ZTracker synth.", Named(Z.Object(selected.synth, "Choose a synth instrument; sampler and drum-kit instruments are not accepted.", v => { if (v != null && ReadInstrument(v)?.family == InstrumentFamily.Synth) AssignDrumItem(new DrumBrowserItem { synth = v, name = InstrumentTitle(v), tag = selected.tag }, selected.noteMin, selected.noteMax); }, 200), "drum-synth"))));
            if (selected.synth != null || selected.keyTracking) box.Add(Named(Z.MicroSlider(selected.synth != null ? "Pitch" : "Root key", selected.baseNote, 0, 119, selected.synth != null ? "Fixed synth pitch for every key in this assignment." : "The key that plays this sample at its original pitch.", v => DrumEdit("drum pitch", () => selected.baseNote = Mathf.RoundToInt(v), false), 180, defaultValue: new DrumMapping().baseNote, decimals: 0), "drum-base-note"));
            box.Add(Named(Z.MicroSlider("Volume", selected.volume, 0, 2, "Level of this drum assignment.", v => DrumEdit("drum volume", () => selected.volume = v, false), 160, defaultValue: new DrumMapping().volume), "drum-volume"));
            if (selected.synth == null) box.Add(Named(Z.Toggle("Pitched range", "Transpose the sample with the played key; off plays the source pitch on every assigned key.", selected.keyTracking, v => DrumEdit("drum pitch tracking", () => selected.keyTracking = v)), "drum-keytracking"));
            else box.Add(Named(Z.MicroSlider("Gate s", selected.gateSeconds, .01f, 2, "Time before the struck synth enters its release stage.", v => DrumEdit("drum gate", () => selected.gateSeconds = v, false), 180, defaultValue: new DrumMapping().gateSeconds), "drum-gate"));
            if (!string.IsNullOrEmpty(selected.sourceUrl)) box.Add(Button("Source ↗", selected.sourceUrl + "\nReveal the original source file.", () => { if (Uri.TryCreate(selected.sourceUrl, UriKind.Absolute, out var uri) && uri.IsFile) EditorUtility.RevealInFinder(uri.LocalPath); }, "drum-source-url"));
        }
    }
}
