using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Wraps GenericMenuPopup content inside a real EditorWindow so the user
    /// can freely resize it via native OS drag handles.
    /// </summary>
    public class GenericMenuEditorWindow : EditorWindow {

        private GenericMenuPopup _popup;
        private EditorWindow previewOwner;

        /// <summary>
        /// Opens a GenericMenuPopup as a resizable EditorWindow instead of a PopupWindow.
        /// </summary>
        public static GenericMenuEditorWindow Show(
            GenericMenu menu,
            string title,
            Vector2 screenPosition,
            List<string> starredPaths,
            string searchTerm = "",
            System.Action<string> onSearchTermChanged = null,
            System.Action<object> onRightClicked = null,
            int columnCount = 3,
            bool invokeNoneSelected = false,
            List<ZoundsEditorPresets.NameListPreset> presetList = null,
            System.Action<System.Action<string, bool>> onDrawCustomFilter = null, EditorWindow previewOwner = null, System.Func<object, EditorWindow, ZoundToken> preview = null) {

            var window = CreateInstance<GenericMenuEditorWindow>();
            window.previewOwner = previewOwner;
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(350, 300);

            var popup = new GenericMenuPopup(menu, title, starredPaths, columnCount, invokeNoneSelected, onDrawCustomFilter);
            popup.onSearchTermChanged = onSearchTermChanged;
            popup._search = searchTerm;
            popup.resizeToContent = false;
            popup.onRightClicked = data => {
                if (preview != null) ZoundPreviewPlayback.Session(window).PlayPreview(data, () => preview(data, window));
                else onRightClicked?.Invoke(data);
            };
            popup.presetList = presetList;
            popup.lastSelectedPresetName = null;
            popup.isResizable = false; // native OS handles resize now

            if (title != null && title.Contains("Add New Klip")) {
                popup._folderFilter = ""; // Ensure starting in All mode
            }

            window._popup = popup;

            // Position near the mouse click
            window.position = new Rect(screenPosition.x, screenPosition.y, 500, 400);
            window.ShowUtility();
            return window;
        }

        private void OnGUI() {
            if (_popup == null) {
                Close();
                return;
            }

            var rect = new Rect(0, 0, position.width, position.height);
            _popup.OnGUI(rect);

            // Close if the popup requested it (e.g. double-click selected)
            if (_popup.WantsToClose) {
                Close();
            }
        }

        private void OnLostFocus() {
            // Mirror PopupWindow behaviour — close when focus is lost
            Close();
        }

        private void OnDisable() => ZoundPreviewPlayback.Dispose(this);

        private void OnDestroy() {
            // Stop only this popup's previews; the launching window owns other previews separately.
            ZoundPreviewPlayback.Dispose(this);
        }
    }

}
