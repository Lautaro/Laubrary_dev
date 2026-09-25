using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class BaseZoundEditorWindow<TZound, TSelf> : EditorWindow where TZound : Zound {

        protected static readonly Dictionary<System.Type, Dictionary<int, BaseZoundEditorWindow<TZound, TSelf>>> allWindows = new Dictionary<System.Type, Dictionary<int, BaseZoundEditorWindow<TZound, TSelf>>>();

        [SerializeField] protected int targetZoundID;

        protected TZound targetZound;
        protected ZoundBrowserEditor<TZound> inspector;
        protected ZoundToken currentToken;

        private bool initialized;

        public bool isLocalZound { get; set; } = false;

        protected bool IsCurrentTokenPlaying() {
            return !(currentToken == null || currentToken.state != ZoundToken.State.Playing);
        }

        public static bool TryGetEditor(TZound target, out TSelf editorWindow) {
            if (allWindows.TryGetValue(typeof(TSelf), out var windows)) {
                if (windows.TryGetValue(target.id, out var window)) {
                    editorWindow = (TSelf)(object)window;
                    return true;
                }
            }
            editorWindow = default;
            return false;
        }

        protected static TWindow OpenWindow<TWindow>(TZound zound, Vector2 minSize) where TWindow : BaseZoundEditorWindow<TZound, TSelf> {
            if (!allWindows.TryGetValue(typeof(TWindow), out var windows)) {
                windows = new Dictionary<int, BaseZoundEditorWindow<TZound, TSelf>>();
                allWindows.Add(typeof(TWindow), windows);
            }
            if (!windows.TryGetValue(zound.id, out var window)) {
                window = CreateInstance<TWindow>();
                window.targetZoundID = zound.id;
                window.minSize = minSize;
                window.Init();
                window.Show();
            }
            else {
                if (window.docked) {
                    window.ShowTab();
                }
                else {
                    window.Focus();
                }
            }
            return (TWindow)window;
        }

        protected virtual TZound FindZoundTarget() {
            return null;
        }

        protected virtual void OnEnable() {
            wantsMouseMove = true;
            autoRepaintOnSceneChange = true;
            Undo.undoRedoPerformed += PerformUndoRedo;

            // ensure init here too to re-register window after recompilation.
            if (ZoundsProject.isJSONLoaded) {
                initialized = true;
                Init();
            }
        }

        protected virtual void OnDisable() {
            Undo.undoRedoPerformed -= PerformUndoRedo;
            OnBaseDisable();
        }

        protected virtual void OnBaseDisable() { }

        private void Init() {
            if (targetZoundID == 0) return;

            var library = ZoundsProject.Instance.zoundLibrary;
            targetZound = FindZoundTarget();
            RefreshWindowName();

            if (allWindows.TryGetValue(GetType(), out var windows)) {
                if (windows.ContainsKey(targetZoundID) && windows[targetZoundID] != this) {
                    windows[targetZoundID] = this;
                }
                else {
                    windows.Add(targetZoundID, this);
                }
            }

            inspector = new ZoundBrowserEditor<TZound>(null);
            OnInit();
        }

        protected void RefreshWindowName() {
            string windowTitle = typeof(TZound).Name + ": ";
            if (targetZound == null) {
                windowTitle += "(Invalid)";
            }
            else {
                windowTitle += targetZound.name;
                if (targetZound is Klip targetKlip && targetKlip.parentId != 0) {
                    if (ZoundDictionary.TryGetZoundById(targetKlip.parentId, out var parentZound)) {
                        windowTitle += " (" + parentZound.name + ")";
                    }
                }
            }
            titleContent.text = windowTitle;
        }

        protected virtual void OnInit() {

        }

        protected virtual void OnFocus() {
            RefreshWindowName();
        }

        protected virtual void OnDestroy() {
            if (allWindows.TryGetValue(GetType(), out var windows)) {
                if (windows.ContainsKey(targetZoundID)) {
                    windows.Remove(targetZoundID);
                }
            }
        }

        private void OnGUI() {
            using var _sheetScope = ZUI.UseSheet("Zounds");
            if (!ZoundsProject.isJSONLoaded) {
                EditorGUILayout.LabelField("Zounds Project is not loaded.");
                return;
            }
            if (!initialized) {
                initialized = true;
                Init();
            }
            if (targetZoundID == 0) {
                Close();
                return;
            }

            // The target lives in the library, which can be reloaded or lose this zound while the
            // window is open; resolve it fresh rather than drawing against a stale or missing object.
            targetZound = FindZoundTarget();
            if (targetZound == null) {
                EditorGUILayout.LabelField(typeof(TZound).Name + " no longer exists in the project.");
                if (GUILayout.Button("Close")) Close();
                return;
            }

            GUILayout.BeginArea(new Rect(0f, 0f, position.width, position.height));
            OnDrawHeader();
            bool remove = OnDrawGUI();
            GUILayout.EndArea();

            if (remove) {
                ZoundsWindow.ModifyZoundsProject("remove zound", () => {
                    AudioAssetUtility.RemoveZound(targetZound);
                }, true);
                Close(); 
                return;
            }

            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Space) {
                OnPressSpaceKey();
            }
        }

        protected virtual void OnPressSpaceKey() {

        }

        protected virtual void OnDrawHeader() {

        }

        protected bool HasAnyInstancePlaying() {
            if (ZoundEngine.CullingGroups.TryGetValue(targetZound, out var playingTokens)) {
                foreach (var playingToken in playingTokens) {
                    if (playingToken != null && playingToken.state == ZoundToken.State.Playing) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Draw the GUI of the window
        /// </summary>
        /// <returns>Returns true if this zound needs to be removed.</returns>
        protected virtual bool OnDrawGUI() {
            return false;
        }

        private void PerformUndoRedo() {
            OnUndoRedoPerformed();
            RefreshWindowName();
            Repaint();
        }

        protected virtual void OnUndoRedoPerformed() {

        }

    }

}
