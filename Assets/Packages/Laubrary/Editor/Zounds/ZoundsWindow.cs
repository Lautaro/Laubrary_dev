using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
#if ADDRESSABLES_INSTALLED
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace Laubrary.Zounds {

    public class ZoundsWindow : ZUIWindow, IHasCustomMenu {

        protected override string ConsumerSheetName => "Zounds";
        protected override string RootBoxStyle => null; // Zounds manages its own root box

        public static ZoundsWindow Instance => instance;
        private static ZoundsWindow instance;
        internal static bool zoundsProjectDirty;

        public static TabViewIMGUI MainTabView => instance != null ? instance.mainTabView : null;

        public static string setFocusNextFrame = null;

        /// <summary>
        /// Opens the old IMGUI Zounds window. Since 2026-09-28 the UI Toolkit window is the main one and owns the
        /// "Laubrary/Zounds Window" menu item; this one is kept, working, for side-by-side comparison and is reached
        /// only from the new window's tab ⋮ menu ("Open IMGUI version").
        /// </summary>
        public static void OpenWindow() {
            var window = GetWindow<ZoundsWindow>();
            window.Show();
        }

        private SerializedObject projectSO;
        private TabViewIMGUI mainTabView;

        private PlayModeStateChange editorState;

        [SerializeField] private TextAsset m_projectJSONAsset;
        private static TextAsset s_projectJSONAsset;

        public TextAsset projectJSONAsset {
            get => m_projectJSONAsset;
            set {
                m_projectJSONAsset = value;
                s_projectJSONAsset = value;
            }
        }

        protected override void OnZUIEnable() {
            instance = this;
            autoRepaintOnSceneChange = true;
            Undo.undoRedoPerformed += PerformUndoRedo;

            var zoundsProject = ZoundsProject.Instance;

            string projectJsonPath = ZoundsProjectInitialization.GetZoundsProjectPath();
            if (!string.IsNullOrEmpty(projectJsonPath)) {
                var assetAtPath = AssetDatabase.LoadAssetAtPath<TextAsset>(projectJsonPath);
                if (assetAtPath != null) {
                    projectJSONAsset = assetAtPath;
                    if (!ZoundsProject.isJSONLoaded) {
                        TriggerLoadJSONProject();
                    }
                }
                else {
                    // Stored path points to a missing file — hard reset to prevent ghost data.
                    projectJSONAsset = null;
                    ZoundsProjectInitialization.SetZoundsProjectPath(string.Empty);
                    ZoundsProject.ResetToDefault();
                }
            }
            else {
                // No path stored — ensure in-memory state is clean.
                projectJSONAsset = null;
                ZoundsProject.ResetToDefault();
            }

            titleContent.text = "Zounds (IMGUI)";
            minSize = new Vector2(414f, 151f);
            saveChangesMessage = "The Zounds project has unsaved changes.";
            if (ZoundsProject.isJSONLoaded) {
                // Deferred so the folder creation + AssetDatabase.Refresh never runs inside a domain reload.
                EditorApplication.delayCall += () => { if (ZoundsProject.isJSONLoaded) ZoundsProject.GenerateDefaultFiles(); };
            }
            zoundsProject.zoundLibrary.Validate();
            projectSO = new SerializedObject(zoundsProject);

            mainTabView = new TabViewIMGUI(new TabContent[] {
                new BrowserTab() { previewOwner = this },
                new MonitorTab() { previewOwner = this },
                new RoutingTab() { previewOwner = this },
                new DependencyMapTab() { previewOwner = this },
                new ProjectSettingsTab() { name = "Settings" },
            });

            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += SaveIfDirtyBeforeReload;
        }

        // Domain reload wipes the in-memory project and re-reads the file, so an unsaved edit would be
        // lost silently. Saving here is the lesser evil; git keeps the previous file state.
        private static void SaveIfDirtyBeforeReload() {
            if (!zoundsProjectDirty) return;
            Debug.Log("[Zounds] Saving unsaved project edits before the domain reload.");
            SaveToJSON();
        }

        public override void SaveChanges() {
            SaveToJSON();
            base.SaveChanges();
        }

        private void Update() {
            if (mainTabView != null) {
                mainTabView.Update();
            }
        }

        private void TriggerLoadJSONProject() {
            ZoundsProject.LoadFromJSON(projectJSONAsset);
            EnsureAudioClipsAddressable();

        }

        private static void EnsureAudioClipsAddressable() {
#if ADDRESSABLES_INSTALLED
            AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
            var projectSettings = ZoundsProject.Instance.projectSettings;
            var allAudioClips = AssetDatabase.FindAssets("t:AudioClip", new string[] {
                projectSettings.sourcesFolderPath,
                projectSettings.workFolderPath,
                projectSettings.libraryFolderPath
            });
            if (addressableSettings == null) return;

            foreach (var guid in allAudioClips) {
                AddressableAssetEntry entry = addressableSettings.FindAssetEntry(guid);
                if (entry == null) {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
#endif
        }

        private void ReloadJSONProject(bool forceReload = false) {
            if (!ZoundsProject.isJSONLoaded || forceReload) {
                if (projectJSONAsset != null) {
                    TriggerLoadJSONProject();
                    if (forceReload) {
                        projectSO = new SerializedObject(ZoundsProject.Instance);
                    }
                }
            }
        }

        private void OnDisable() {
            ZoundPreviewPlayback.Dispose(this);
            AssemblyReloadEvents.beforeAssemblyReload -= SaveIfDirtyBeforeReload;
            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;
            Undo.undoRedoPerformed -= PerformUndoRedo;
            mainTabView?.GetTab<BrowserTab>(0)?.Dispose();
            mainTabView?.GetTab<MonitorTab>(0)?.Dispose();
        }

        private void EditorApplication_playModeStateChanged(PlayModeStateChange stateChange) {
            editorState = stateChange;
            if (editorState == PlayModeStateChange.ExitingEditMode && zoundsProjectDirty) {
                // On projects that reload the domain on Play, the in-memory project would be lost here.
                SaveIfDirtyBeforeReload();
            }
            if (editorState == PlayModeStateChange.ExitingPlayMode) {
                ZoundEngine.PersistMissingZounds();
            }
            if (editorState == PlayModeStateChange.EnteredEditMode || editorState == PlayModeStateChange.EnteredPlayMode) {
                Repaint();
            }
        }

        protected override void OnZUI() {
            s_projectJSONAsset = m_projectJSONAsset;
            if (setFocusNextFrame != null) {
                GUI.FocusControl(setFocusNextFrame);
                setFocusNextFrame = null;
            }
            if (editorState == PlayModeStateChange.ExitingEditMode || editorState == PlayModeStateChange.ExitingPlayMode) {
                return;
            }
            if (projectSO == null || projectSO.targetObject == null) {
                if (ZoundsProject.Instance != null) {
                    projectSO = new SerializedObject(ZoundsProject.Instance);
                }
                else {
                    return;
                }
            }
            projectSO.Update();
            
            // DrawJSONProjectField(); // Moved into browser settings

            using (this.Box(null, "Alternative"))
            {
                if (ZoundsProject.isJSONLoaded) {
                    var contentRect = new Rect(0, 0, position.width, position.height);
                    int selectedMainTab = ZoundsWindowProperties.Instance.selectedMainTab;
                    int tempMainTab = mainTabView.DrawLayout(selectedMainTab, projectSO, contentRect);
                    if (tempMainTab != selectedMainTab) {
                        Undo.RecordObject(ZoundsWindowProperties.Instance, "change selected main tab");
                        ZoundsWindowProperties.Instance.selectedMainTab = tempMainTab;
                        EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
                    }
                }
                else
                {
                    GUILayout.Space(30f);
                    GUILayout.Label("No Zounds Project Loaded", EditorStyles.boldLabel);
                    DrawJSONProjectField();
                }
            }

            if (projectSO.ApplyModifiedProperties()) {
                SetZoundsProjectDirty();
            }
        }

        public void DrawJSONProjectField() {
            GUILayout.BeginHorizontal();
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 80f;
            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField("Project JSON", projectJSONAsset, typeof(TextAsset), false) as TextAsset;
            if (EditorGUI.EndChangeCheck()) {
                AssignProjectJSON(newTarget);
            }
            EditorGUIUtility.labelWidth = labelWidth;
            var guiEnabled = GUI.enabled;
            if (GUILayout.Button("Create New", GUILayout.Width(85f))) {
                CreateNewProject();
            }
            GUI.enabled = guiEnabled && !ReferenceEquals(projectJSONAsset, null);
            if (GUILayout.Button("Load", GUILayout.Width(60f))) {
                LoadProject();
            }
            EditorGUIUtility.labelWidth = 65f;
            {
                var saveEnabled = guiEnabled && projectJSONAsset != null;
                GUI.enabled = saveEnabled;
                EditorGUI.BeginChangeCheck();
                var autoSave = EditorGUILayout.Toggle("Auto-Save", ZoundsWindowProperties.Instance.autoSave, GUILayout.Width(82f));
                if (EditorGUI.EndChangeCheck()) {
                    SetAutoSave(autoSave);
                }
                EditorGUIUtility.labelWidth = labelWidth;
                GUI.enabled = saveEnabled && zoundsProjectDirty;
                if (GUILayout.Button("Save", GUILayout.Width(60f))) {
                    SaveToJSON();
                }
            }
            GUI.enabled = guiEnabled;
            GUILayout.EndHorizontal();
        }

        // ── The project-file row's actions (Project JSON field, Create New, Load, Auto-Save), shared with the UI Toolkit
        //    twin (T-0470). They act on the open Zounds window when there is one, and on the stored project path otherwise.

        /// <summary>The project JSON the Zounds window works on: the open window's, else the one the stored path names.</summary>
        internal static TextAsset CurrentProjectJSON {
            get {
                if (instance != null) return instance.projectJSONAsset;
                if (s_projectJSONAsset != null) return s_projectJSONAsset;
                string path = ZoundsProjectInitialization.GetZoundsProjectPath();
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
        }

        static void SetProjectJSONAsset(TextAsset asset) {
            s_projectJSONAsset = asset;
            if (instance != null) instance.projectJSONAsset = asset;
        }

        static void AfterProjectChange() {
            if (instance != null) {
                instance.mainTabView?.GetTab<BrowserTab>(0)?.RefreshFilters();
                instance.Repaint();
            }
        }

        static void LoadJSONProject(TextAsset asset) {
            ZoundsProject.LoadFromJSON(asset);
            EnsureAudioClipsAddressable();
        }

        /// <summary>The Project JSON field changed: persist the path and load it, or with none, clear everything.</summary>
        internal static void AssignProjectJSON(TextAsset newTarget) {
            if (instance != null) Undo.RecordObject(instance, "change project resource path");
            SetProjectJSONAsset(newTarget);
            if (instance != null) EditorUtility.SetDirty(instance);

            if (newTarget != null) {
                // Assignment: persist path and immediately load.
                string assetPath = AssetDatabase.GetAssetPath(newTarget);
                ZoundsProjectInitialization.SetZoundsProjectPath(assetPath);
                LoadJSONProject(newTarget);
            }
            else {
                // Clear: wipe stored path and reset all in-memory data.
                ZoundsProjectInitialization.SetZoundsProjectPath(string.Empty);
                ZoundsProject.ResetToDefault();
                zoundsProjectDirty = false;

                // Clean up the build project to prevent stale builds.
                string streamingAssetsPath = "Assets/StreamingAssets/DefaultZoundsProject.json";
                if (File.Exists(Path.Combine(Application.dataPath, "StreamingAssets/DefaultZoundsProject.json"))) {
                    AssetDatabase.DeleteAsset(streamingAssetsPath);
                }
            }
            AfterProjectChange();
        }

        /// <summary>"Create New": a fresh project file next to the assets root, made the current one and loaded.</summary>
        internal static void CreateNewProject() {
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ZoundsProject.json");
            SaveToJSON(uniquePath, new ZoundsProject.ProjectSerializer());
            SetProjectJSONAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(uniquePath));
            ZoundsProjectInitialization.SetZoundsProjectPath(uniquePath);
            ZoundsProject.GenerateDefaultFiles();
            LoadJSONProject(CurrentProjectJSON);
            AfterProjectChange();
            zoundsProjectDirty = false;
        }

        /// <summary>"Load": re-reads the current project file, dropping unsaved edits.</summary>
        internal static void LoadProject() {
            var asset = CurrentProjectJSON;
            if (asset == null) {
                EditorUtility.DisplayDialog("Load Zounds Project Failed", "File not found: " + asset, "Close");
            }
            else {
                LoadJSONProject(asset);
                AfterProjectChange();
                zoundsProjectDirty = false;
            }
        }

        /// <summary>The Auto-Save toggle.</summary>
        internal static void SetAutoSave(bool autoSave) {
            Undo.RecordObject(ZoundsWindowProperties.Instance, "toggle auto-save");
            ZoundsWindowProperties.Instance.autoSave = autoSave;
            EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
        }

        private void PerformUndoRedo() {
#if ADDRESSABLES_INSTALLED
            ZoundsAssetPostProcessor.RefreshAudioClipsCache();
#endif
            string assetPath;
            if (projectJSONAsset != null) assetPath = AssetDatabase.GetAssetPath(projectJSONAsset);
            else assetPath = "";
            ZoundsProjectInitialization.SetZoundsProjectPath(assetPath);
            ZoundsWindowProperties.DirtyAll();
            // Write the reverted in-memory state back to JSON so disk and memory stay in sync.
            // Without this, the next SaveToJSON from any future edit would re-read stale disk state.
            SaveToJSON();
            // repaint immediately when user undo/redo to make experience feels more fluid
            Repaint();
        }

        public static void RepaintWindow() {
            if (instance != null) {
                var zoundBrowserTab = instance.mainTabView.GetTab<BrowserTab>(0);
                zoundBrowserTab.RefreshFilters();
                instance.Repaint();
            }
        }

        public static void PingWindow() {
            if (instance == null) {
                OpenWindow();
            }
            else {
                instance.ShowTab();
            }
        }

        // Implemention for IHasCustomMenu to add menu toggle in top right window menu
        public void AddItemsToMenu(GenericMenu menu) {
            menu.AddItem(new GUIContent("Grid Mode (Zounds Browser)"), ZoundsProject.Instance.browserSettings.multicolumn, ToggleColumnView);
        }

        internal static void ToggleColumnView() {
            ModifyZoundsProject("toggle column view", () => {
                ZoundsProject.Instance.browserSettings.multicolumn = !ZoundsProject.Instance.browserSettings.multicolumn;
            });
        }

        public static void SetZoundsProjectDirty() {
            zoundsProjectDirty = true;
            if (instance != null) instance.hasUnsavedChanges = true;
        }

        private static bool s_isModifying = false;
        private static int s_dragUndoGroup = -1;
        private static int s_joinGroup = -1;

        /// <summary>
        /// The next edit (a drag, or a modification) records into Undo group <paramref name="group"/> instead of a new one,
        /// provided nothing else has moved the current group on meanwhile -- so an edit that first had to switch the editor
        /// to a copy of a shared sound (destructive editing, 2026-10-09) undoes together with that switch, in one step.
        /// </summary>
        internal static void JoinNextEdit(int group) { s_joinGroup = group; }

        static bool TakeJoinedGroup(out int group) {
            group = s_joinGroup;
            s_joinGroup = -1;
            return group >= 0 && group == Undo.GetCurrentGroup();
        }

        /// <summary>
        /// Opens a named undo group and records the ZoundsProject snapshot.
        /// Uses RegisterCompleteObjectUndo instead of RecordObject because
        /// drag mutations happen on subsequent frames — RecordObject's frame-end
        /// diff would find no change on the MouseDown frame and silently discard
        /// the entry. RegisterCompleteObjectUndo stores the full state immediately.
        /// Must be paired with a call to EndDragUndo on MouseUp.
        /// </summary>
        public static void BeginDragUndo(string undoName) {
            if (!TakeJoinedGroup(out s_dragUndoGroup)) {
                Undo.IncrementCurrentGroup();
                s_dragUndoGroup = Undo.GetCurrentGroup();
            }
            Undo.RegisterCompleteObjectUndo(ZoundsProject.Instance, undoName);
            Undo.SetCurrentGroupName(undoName);
            s_isModifying = true;
        }

        /// <summary>
        /// Closes the undo group opened by BeginDragUndo, persists to JSON,
        /// and collapses everything into the single named entry.
        /// Safe to call even if BeginDragUndo was never called (no-op).
        /// </summary>
        public static void EndDragUndo(System.Action action = null) {
            if (s_dragUndoGroup < 0) return;
            try {
                action?.Invoke();
                EditorUtility.SetDirty(ZoundsProject.Instance);
                SaveToJSON();
            }
            finally {
                s_isModifying = false;
                Undo.CollapseUndoOperations(s_dragUndoGroup);
                s_dragUndoGroup = -1;
            }
            RaiseEditCommitted();
        }

        /// <summary>
        /// Raised once an edit to the project is COMMITTED: at the end of a drag (the release that closes its undo step),
        /// or at the end of a discrete edit made outside a drag. Never while a drag is still going. Workflow tools that
        /// react to "the user just changed something" (the editors' Play on change, T-0486) listen here instead of to every
        /// intermediate value a drag passes through.
        /// </summary>
        public static event System.Action onEditCommitted;

        static void RaiseEditCommitted() {
            try { onEditCommitted?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        public static void ModifyZoundsProject(string undoMessage, System.Action action, bool repaintWindow = false) {
            ModifyZoundsProject(undoMessage, action, repaintWindow, forceSave: false);
        }

        public static void ModifyZoundsProject(string undoMessage, System.Action action, bool repaintWindow, bool forceSave) {
            var zoundsProject = ZoundsProject.Instance;

            bool isOutermost = !s_isModifying;
            int undoGroup = -1;

            if (isOutermost) {
                s_isModifying = true;
                if (!TakeJoinedGroup(out undoGroup)) {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                }
            }

            try {
                Undo.RecordObject(zoundsProject, undoMessage);
                action.Invoke();
                EditorUtility.SetDirty(zoundsProject);
                ZoundsProject.NotifyModified();
                if (forceSave || ZoundsWindowProperties.Instance.autoSave) {
                    SaveToJSON();
                }
                else {
                    SetZoundsProjectDirty();
                }
            }
            finally {
                if (isOutermost) {
                    s_isModifying = false;
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.SetCurrentGroupName(undoMessage);
                    if (repaintWindow) {
                        RepaintWindow();
                    }
                }
            }
            // A discrete edit is committed as it happens; one made inside an open drag is committed by the drag's release.
            if (isOutermost && s_dragUndoGroup < 0) RaiseEditCommitted();
        }

        /// <summary>
        /// Like ModifyZoundsProject but ALWAYS persists to JSON immediately, regardless of autoSave.
        /// Use this for Klip Editor changes so edits survive domain reload, play mode, and window close.
        /// </summary>
        public static void ModifyAndSaveZoundsProject(string undoMessage, System.Action action, bool repaintWindow = false) {
            ModifyZoundsProject(undoMessage, action, repaintWindow, forceSave: true);
        }

        public static void SaveToJSON() {
            // The asset reference is set by the Zounds window while it is open. Another tool (the Zound
            // Routing window, a probe) can modify the project while it is closed, so fall back to the
            // project path the initialization keeps; a save that cannot find its file is an error, not a no-op.
            if (!ZoundsProject.isJSONLoaded) {
                Debug.LogError("[Zounds] Project modified before any project JSON was loaded, so nothing was saved: writing now would replace the file with an empty project.");
                return;
            }
            if (s_projectJSONAsset == null) {
                string configuredPath = ZoundsProjectInitialization.GetZoundsProjectPath();
                if (!string.IsNullOrWhiteSpace(configuredPath))
                    s_projectJSONAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(configuredPath);
                if (s_projectJSONAsset == null) {
                    Debug.LogError("[Zounds] Project modified but no project JSON asset is known, so nothing was saved. Configured path: '" + configuredPath + "'.");
                    return;
                }
            }

            // Ensure all Klips have output clips in ZoundFiles/ before persisting.
            EnsureAllKlipOutputs();

            zoundsProjectDirty = false;
            if (instance != null) instance.hasUnsavedChanges = false;
            string assetPath = AssetDatabase.GetAssetPath(s_projectJSONAsset);

            var zoundsProject = ZoundsProject.Instance;
            var serializer = new ZoundsProject.ProjectSerializer() {
                browserSettings = zoundsProject.browserSettings,
                projectSettings = zoundsProject.projectSettings,
                zoundLibrary = zoundsProject.zoundLibrary,
                zoundRoutings = zoundsProject.zoundRoutings
            };
            SaveToJSON(assetPath, serializer);
        }

        public static string StringifyToJSON() {
            var zoundsProject = ZoundsProject.Instance;
            var serializer = new ZoundsProject.ProjectSerializer() {
                browserSettings = zoundsProject.browserSettings,
                projectSettings = zoundsProject.projectSettings,
                zoundLibrary = zoundsProject.zoundLibrary,
                zoundRoutings = zoundsProject.zoundRoutings
            };
            return JsonUtility.ToJson(serializer, true);
        }

        private static bool s_isEnsuringOutputs = false;

        /// <summary>
        /// Sweeps all Klips and ensures each one whose source cannot ship as-is has an output clip
        /// in ZoundFiles/ (see <see cref="ZoundsClipClassifier.NeedsOutputClip"/>). Klips with a
        /// Library source are left alone: they play the source directly.
        /// Called automatically before every edit-mode JSON save.
        /// </summary>
        private static void EnsureAllKlipOutputs() {
            // File copies + AssetDatabase imports freeze the editor; defer to the next edit-mode save
            // and the pre-build audit (ZoundsPreprocessBuild) instead of running mid-Play.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (s_isEnsuringOutputs) return; // Guard against re-entry from PromoteOutputClip's ModifyZoundsProject calls.
            s_isEnsuringOutputs = true;
            try {
#if ADDRESSABLES_INSTALLED
                var library = ZoundsProject.Instance.zoundLibrary;
                var settings = ZoundsProject.Instance.projectSettings;
                int promoted = 0;
                library.ForEachZound(z => {
                    if (z is Klip klip && ZoundsClipClassifier.NeedsOutputClip(klip, settings)) {
                        KlipEditorWindow.PromoteOutputClip(klip);
                        promoted++;
                    }
                    return false;
                });
                if (promoted > 0) {
                    Debug.Log($"[Zounds] Auto-ensured output clips for {promoted} Klip(s).");
                }
#endif
            }
            finally {
                s_isEnsuringOutputs = false;
            }
        }

        // Prevents ZoundsAssetPostProcessor from reloading the JSON we just wrote,
        // which would overwrite in-memory state with stale disk content.
        internal static bool isSavingJSON = false;

        private static void SaveToJSON(string assetPath, ZoundsProject.ProjectSerializer serializer) {
            string fullJSONPath = Path.Combine(
                Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length),
                assetPath
            );
            string content = JsonUtility.ToJson(serializer, true);
            isSavingJSON = true;
            try {
                File.WriteAllText(fullJSONPath, content);
                AssetDatabase.ImportAsset(assetPath);
                s_projectJSONAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (instance != null) {
                    instance.m_projectJSONAsset = s_projectJSONAsset;
                }
            }
            finally {
                isSavingJSON = false;
            }
        }


    }

}
