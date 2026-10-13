using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>
    /// ZTracker reopens as it was closed (T-0608): the same song, instrument, view (Pattern, Mixer, Automation,
    /// Instrument, Song), the same sequence slot, cursor, track, tabs and selections, and each page's folds and scroll
    /// positions. Script reloads and editor restarts already keep an open window's fields (Unity reserializes the
    /// layout); this covers the window being closed and opened again from its menu. The state is one small per-user
    /// file in the project's UserSettings folder (never committed, never inside a song or instrument, so viewing
    /// never dirties an asset). Only the window the user opened from the menu reads and writes it; check and walk
    /// windows never do. Anything the file names that no longer exists is skipped quietly.
    /// </summary>
    public sealed partial class ZTrackerModelWindow
    {
        [Serializable]
        sealed class ViewState
        {
            public int version = 1;
            public string songGuid = "", instrumentGuid = "";
            public int pane, order, row, track, sub, entryInstrument, octave = DefaultPatternOctave, step = DefaultPatternStep;
            public bool follow = true, editMode = true;
            public int instrumentTab, macroIndex, presetIndex = -1, instrumentChain, sampleSort, mixerTab;
            public string sampleId = "", zoneId = "", modSetId = "", modDeviceId = "";
            public int automationLane, automationTarget, automationPoint;
            public string drumId = "", drumSearch = "", drumTagFilter = "";
            public int drumSourceTab, drumSort, drumPage, drumFirstOctave = 3, drumLow = 36, drumHigh = 36;
            public List<WorkspacePage> workspacePages = new List<WorkspacePage>();
        }

        // The menu-opened window remembers its view; set by Open(). Serialized so it survives a script reload.
        [SerializeField] bool rememberView;
        // A window opened from the menu before this existed is marked as the user's by its MIDI preferences flag.
        bool RemembersView => rememberView || midiPreferenceRestoreEnabled;
        // This window already shows a view (restored once, or kept by Unity through a reload or a restart).
        [SerializeField] bool viewRestored;
        // Workspace pages kept in the file; older pages beyond this are dropped so the file cannot grow without bound.
        const int MaxSavedPages = 48;

        internal static string viewStatePathOverride;
        static string ViewStatePath => viewStatePathOverride ?? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UserSettings", "ZTracker", "ViewState.json");

        static string GuidOf(UnityEngine.Object value)
        {
            string path = value == null ? null : AssetDatabase.GetAssetPath(value);
            return string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>Writes this window's view to the per-user file (the menu-opened window only).</summary>
        internal void SaveViewState()
        {
            if (!RemembersView) return;
            try
            {
                CaptureWorkspace();
                var state = new ViewState
                {
                    songGuid = GuidOf(song), instrumentGuid = GuidOf(instrument),
                    pane = pane, order = order, row = row, track = track, sub = sub, entryInstrument = entryInstrument, octave = octave, step = step,
                    follow = follow, editMode = editMode,
                    instrumentTab = instrumentTab, macroIndex = macroIndex, presetIndex = presetIndex, instrumentChain = instrumentChain, sampleSort = sampleSort, mixerTab = mixerTab,
                    sampleId = sampleId, zoneId = zoneId, modSetId = modSetId, modDeviceId = modDeviceId,
                    automationLane = automationLane, automationTarget = automationTarget, automationPoint = automationPoint,
                    drumId = drumId, drumSearch = drumSearch, drumTagFilter = drumTagFilter,
                    drumSourceTab = drumSourceTab, drumSort = drumSort, drumPage = drumPage, drumFirstOctave = drumFirstOctave, drumLow = drumLow, drumHigh = drumHigh,
                };
                // The page shown now first, then the most recent others.
                var pages = new List<WorkspacePage>(workspacePages ?? new List<WorkspacePage>());
                string current = displayedWorkspace;
                pages.Sort((a, b) => (b.key == current).CompareTo(a.key == current));
                state.workspacePages = pages.GetRange(0, Math.Min(MaxSavedPages, pages.Count));
                string path = ViewStatePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json = JsonUtility.ToJson(state, true);
                if (!File.Exists(path) || File.ReadAllText(path) != json) File.WriteAllText(path, json);
            }
            catch (Exception e) { Debug.LogWarning("ZTracker could not save its view: " + e.Message); }
        }

        /// <summary>
        /// Puts back the view saved when ZTracker was last closed. Missing or damaged state, a song or instrument that
        /// no longer exists, and indices past the end of a changed song all fall back quietly (the song browser,
        /// no instrument, the nearest valid slot, track and row).
        /// </summary>
        internal bool RestoreViewState()
        {
            string path = ViewStatePath;
            ViewState state;
            try { if (!File.Exists(path)) return false; state = JsonUtility.FromJson<ViewState>(File.ReadAllText(path)); }
            catch (Exception) { return false; }
            if (state == null || state.version != 1) return false;
            var savedSong = string.IsNullOrEmpty(state.songGuid) ? null : AssetDatabase.LoadAssetAtPath<ZTrackerSong>(AssetDatabase.GUIDToAssetPath(state.songGuid));
            if (savedSong == null) return false;   // the song is gone: keep whatever the window opened on (or the browser)
            asset = savedSong;
            OnAssetChanged();
            var savedInstrument = string.IsNullOrEmpty(state.instrumentGuid) ? null : AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(AssetDatabase.GUIDToAssetPath(state.instrumentGuid));
            var data = Data;
            int instruments = data?.instruments?.Count ?? 0, slots = data?.sequence?.Count ?? 0, tracks = data?.tracks?.Count ?? 0;
            pane = Mathf.Clamp(state.pane, 0, 4);
            order = Mathf.Clamp(state.order, 0, Math.Max(0, slots - 1));
            track = Mathf.Clamp(state.track, 0, Math.Max(0, tracks - 1));
            var pattern = Pattern;
            row = Mathf.Clamp(state.row, 0, Math.Max(0, (pattern?.lineCount ?? 1) - 1));
            sub = Math.Max(0, state.sub);   // checked against the laid-out columns when the grid is built
            entryInstrument = Mathf.Clamp(state.entryInstrument, 0, Math.Max(0, instruments - 1));
            instrument = savedInstrument;
            octave = Mathf.Clamp(state.octave, 0, 9); step = Mathf.Clamp(state.step, 0, 64);
            follow = state.follow; editMode = state.editMode;
            instrumentTab = Math.Max(0, state.instrumentTab); macroIndex = Mathf.Clamp(state.macroIndex, 0, 7); presetIndex = state.presetIndex;
            instrumentChain = Math.Max(0, state.instrumentChain); sampleSort = state.sampleSort; mixerTab = Mathf.Clamp(state.mixerTab, 0, 2);
            sampleId = state.sampleId ?? ""; zoneId = state.zoneId ?? ""; modSetId = state.modSetId ?? ""; modDeviceId = state.modDeviceId ?? "";
            automationLane = Math.Max(0, state.automationLane); automationTarget = Math.Max(0, state.automationTarget); automationPoint = Math.Max(0, state.automationPoint);
            drumId = state.drumId ?? ""; drumSearch = state.drumSearch ?? ""; drumTagFilter = state.drumTagFilter ?? "";
            drumSourceTab = state.drumSourceTab; drumSort = state.drumSort; drumPage = Math.Max(0, state.drumPage);
            drumFirstOctave = state.drumFirstOctave; drumLow = state.drumLow; drumHigh = state.drumHigh;
            if (state.workspacePages != null && state.workspacePages.Count > 0) workspacePages = state.workspacePages;
            return true;
        }

        void OnViewQuitting() => SaveViewState();
    }
}
