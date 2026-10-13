using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.AssetKit.Editor;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>
    /// ZTracker's songs and instruments (drum kits are instruments) as LauAssets (T-0607): the same registry, browser,
    /// picker, create, duplicate, rename, tags and open-in-editor every other Laubrary tool's assets use. The song is
    /// the window's own asset (the AssetKit window base supplies its toolbar and browser); instruments are picked
    /// through the shared LauAsset browser and get the same file actions and tags in the Instrument view.
    /// </summary>
    public sealed partial class ZTrackerModelWindow
    {
        // ───────────── registry: Open / Create, double-click ─────────────

        [InitializeOnLoadMethod]
        static void RegisterLauAssets()
        {
            LauAssetEditors.RegisterOpen<ZTrackerSong>(OpenFor);
            LauAssetEditors.RegisterOpen<ZTrackerInstrument>(OpenInstrument);
            LauAssetEditors.RegisterCreate<ZTrackerSong>(CreateSongAsset);
            LauAssetEditors.RegisterCreate<ZTrackerInstrument>(CreateInstrumentAsset);
        }

        /// <summary>Double-clicking a song or an instrument in the Project window opens it in ZTracker.</summary>
        [OnOpenAsset]
        static bool OnOpenTrackerAsset(int instanceId, int line)
        {
            switch (EditorUtility.EntityIdToObject(instanceId))
            {
                case ZTrackerSong s: OpenFor(s); return true;
                case ZTrackerInstrument i: OpenInstrument(i); return true;
                default: return false;
            }
        }

        /// <summary>A new saved song with the same content as the window's New (one note track, the master, one pattern).</summary>
        public static ZTrackerSong CreateSongAsset(string name, string folder)
        {
            var created = AssetLibrary<ZTrackerSong>.Create(string.IsNullOrWhiteSpace(name) ? "Song" : name, string.IsNullOrEmpty(folder) ? "Assets/ZTracker" : folder);
            if (created == null) return null;
            SeedSong(created);
            created.model.name = created.name;
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssetIfDirty(created);
            return created;
        }

        /// <summary>A new saved synth instrument (the same defaults as + Instrument), not yet in any song.</summary>
        public static ZTrackerInstrument CreateInstrumentAsset(string name, string folder)
        {
            var created = AssetLibrary<ZTrackerInstrument>.Create(string.IsNullOrWhiteSpace(name) ? "Instrument" : name, string.IsNullOrEmpty(folder) ? "Assets/ZTracker" : folder);
            if (created == null) return null;
            created.schemaVersion = 1;
            created.model = NewInstrumentData();
            created.model.name = created.name;
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssetIfDirty(created);
            return created;
        }

        /// <summary>
        /// Opens an instrument in ZTracker's Instrument view: in the open song when it plays there, otherwise in the
        /// first song of the project that uses it; when no song uses it, the open song stays and the instrument is
        /// edited on its own (+ Add… puts it in the song).
        /// </summary>
        public static void OpenInstrument(ZTrackerInstrument value)
        {
            Open();
            var window = GetWindow<ZTrackerModelWindow>("ZTracker");
            if (value == null) return;
            bool InSong(ZTrackerSong s) => s != null && s.schemaVersion == 1 && s.model?.instruments != null && s.model.instruments.Contains(value);
            if (!InSong(window.song))
            {
                var owner = AssetLibrary<ZTrackerSong>.Enumerate().FirstOrDefault(InSong);
                if (owner != null) window.SetAsset(owner);
            }
            window.SelectInstrument(value);
        }

        void SelectInstrument(ZTrackerInstrument value)
        {
            instrument = value;
            int at = Data?.instruments?.IndexOf(value) ?? -1;
            if (at >= 0) entryInstrument = at;
            pane = 3;
            Rebuild();
        }

        // ───────────── instruments: pick through the shared browser ─────────────

        /// <summary>"Add…": the shared LauAsset browser of every instrument (search, sort, tags, pictures); the pick joins the song.</summary>
        VisualElement AddInstrumentButton()
        {
            Button add = null;
            add = Button("Add…", "Pick an instrument or drum kit from the project (the shared asset browser: search, sort, tags), or make a new one there, and add it to this song.", () =>
            {
                var wb = add.worldBound;
                LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), typeof(ZTrackerInstrument), picked => AddInstrument(picked as ZTrackerInstrument), null,
                    name => AddInstrument(CreateInstrumentAsset(name, "Assets/ZTracker")), "Pick an instrument for this song, or type a name and Save as new.");
            }, "add-instrument");
            return add;
        }

        internal void AddInstrument(ZTrackerInstrument picked)
        {
            if (picked == null || Data == null) return;
            if (!Data.instruments.Contains(picked)) SongEdit("add instrument", () => Data.instruments.Add(picked), true);
            entryInstrument = Data.instruments.IndexOf(picked);
            instrument = picked;
            BuildPane();
        }

        /// <summary>A reference to one instrument, drawn as the shared LauAsset chip but over a chosen set of candidates.</summary>
        VisualElement InstrumentChip(ZTrackerInstrument current, Func<ZTrackerInstrument, bool> allowed, string tooltip, Action<ZTrackerInstrument> picked)
        {
            var chip = new ZuiChip(current != null ? current.name : null, tooltip, empty: current == null);
            if (current != null) { var tex = LauAssetGridGUI.GetThumbnail(current, null, chipThumbs); if (tex != null) chip.Thumbnail = tex; }
            chip.OnActivate = c =>
            {
                var wb = c.worldBound;
                var items = AssetLibrary<ZTrackerInstrument>.Enumerate().Where(i => allowed(i)).Cast<Object>().ToList();
                LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), items, o => picked(o as ZTrackerInstrument), current);
            };
            chip.Accepts = o => o is ZTrackerInstrument i && allowed(i);
            chip.OnDrop = o => picked(o as ZTrackerInstrument);
            return chip;
        }

        readonly Dictionary<Object, Texture2D> chipThumbs = new Dictionary<Object, Texture2D>();

        // ───────────── instrument file actions and tags (the song's are in the window toolbar) ─────────────

        bool renamingInstrument;
        string instrumentRenameText = "";

        /// <summary>
        /// The instrument's own file actions, on its header row beside the display name, matching the song's toolbar:
        /// the file name, Rename, Duplicate (the copy joins the song beside it), Select in Project, and its Tags (the
        /// shared tag picker and side table, filterable in every asset browser).
        /// </summary>
        void AddInstrumentAssetControls(VisualElement row)
        {
            var asset = instrument;
            string path = asset == null ? null : AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsSubAsset(asset)) return;
            if (renamingInstrument)
            {
                TextField field = null;
                void Confirm()
                {
                    renamingInstrument = false;
                    string typed = (instrumentRenameText ?? "").Trim();
                    // One name for the instrument: the list, the pattern and the asset browsers all show the same one.
                    // The display name is an Undo step; the file rename is file work and is not.
                    if (typed.Length > 0 && AssetLibrary<ZTrackerInstrument>.Rename(asset, typed) && asset.schemaVersion == 1 && asset.model != null && asset.model.name != asset.name)
                    { var renamed = asset; var keep = instrument; instrument = renamed; InstrumentEdit("instrument name", () => renamed.model.name = renamed.name); instrument = keep; }
                    BuildPane();
                }
                field = Named(Z.TextInput(instrumentRenameText, "New file name for this instrument.", v => instrumentRenameText = v, 180), "instrument-rename-field");
                field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Confirm(); e.StopPropagation(); } else if (e.keyCode == KeyCode.Escape) { renamingInstrument = false; BuildPane(); e.StopPropagation(); } }, TrickleDown.TrickleDown);
                field.schedule.Execute(() => field.Focus());
                row.Add(field);
                row.Add(Button("OK", "Apply the new file name.", Confirm, "instrument-rename-ok"));
                row.Add(Button("Cancel", "Keep the current file name.", () => { renamingInstrument = false; BuildPane(); }, "instrument-rename-cancel"));
            }
            else
            {
                var name = Named(Z.Text(Path.GetFileNameWithoutExtension(path), tooltip: "The instrument's file: " + path + "."), "instrument-file-name");
                name.style.maxWidth = 180; name.style.overflow = Overflow.Hidden; name.style.textOverflow = TextOverflow.Ellipsis; name.style.whiteSpace = WhiteSpace.NoWrap; name.style.opacity = .75f;
                // Only when the file is named differently from the instrument (an older asset); otherwise it says nothing new.
                if (asset.model == null || asset.model.name != Path.GetFileNameWithoutExtension(path)) row.Add(name);
                row.Add(Button("Rename", "Rename this instrument: its file and the name shown in the song together.", () => { renamingInstrument = true; instrumentRenameText = Path.GetFileNameWithoutExtension(path); BuildPane(); }, "instrument-rename"));
                row.Add(Button("Duplicate", "Copy this instrument's file next to it, add the copy to this song and edit the copy.", () => DuplicateInstrument(asset), "instrument-duplicate"));
                row.Add(Named(Z.IconButton("magnifying-glass", "Select this instrument's file in the Project window.", () => { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }), "instrument-ping"));
            }
            var tags = new IMGUIContainer(() => LauTagField.Draw(asset)) { name = "instrument-tags", tooltip = "Tags for this instrument, filterable in every asset browser." };
            tags.AddToClassList("lau-asset-browser__tag-island");
            tags.style.width = 200; tags.style.flexShrink = 0;
            row.Add(tags);
        }

        internal void DuplicateInstrument(ZTrackerInstrument source)
        {
            if (source == null) return;
            var copy = AssetLibrary<ZTrackerInstrument>.Duplicate(source);
            if (copy == null) return;
            // The copy is a separate instrument: its own identity, so commands and the engine never mistake it for the original.
            if (copy.schemaVersion == 1 && copy.model != null) { copy.model.id = Id(); copy.model.name = copy.name; EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy); }
            Undo.RegisterCreatedObjectUndo(copy, "Tracker: duplicate instrument");
            if (Data != null && Data.instruments.Contains(source)) AddInstrument(copy); else { instrument = copy; BuildPane(); }
        }

        // ───────────── the song's tags (song file actions are in the window toolbar) ─────────────

        /// <summary>The song's tags (the shared tag picker and side table, filterable in the song browser), beside its title.</summary>
        VisualElement SongTags()
        {
            if (song == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(song)) || AssetDatabase.IsSubAsset(song)) return null;
            var edited = song;
            var tags = new IMGUIContainer(() => LauTagField.Draw(edited)) { name = "song-tags", tooltip = "Tags for this song, filterable in the song browser." };
            tags.AddToClassList("lau-asset-browser__tag-island");
            tags.style.width = 200; tags.style.flexShrink = 0;
            return tags;
        }
    }
}
