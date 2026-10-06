using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Popover for adding a new zound. Exposes a type choice (Klip / Zequence) and a name
    /// text field pre-filled with a unique suggestion. On submit, routes to the same
    /// creation paths used by OpenAddNewZoundMenu so behavior matches the legacy menu.
    /// </summary>
    public class AddZoundPopup : PopupWindowContent {

        enum ZoundKind { Klip, Zequence }

        private ZoundKind kind = ZoundKind.Klip;
        private string name;
        private EditorWindow previewOwner;
        private Vector2 triggerMouseGui;   // GUI-space (pre-screen conversion), captured at Show
                                            // time; forwarded to OpenCreateNewKlipDialogExternal
                                            // which internally converts to screen coords.
        private bool focusedOnce = false;

        private const string k_NameFieldCtrl = "addzound_name_field";

        public static void Show(Rect activatorRect, EditorWindow previewOwner) {
            var popup = new AddZoundPopup { previewOwner = previewOwner };
            // Capture GUI-space mouse position for later forwarding to the Klip dialog, which
            // does the GUI→screen conversion itself (matches legacy OpenAddNewZoundMenu flow).
            popup.triggerMouseGui = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            popup.name = SuggestName(popup.kind);
            PopupWindow.Show(activatorRect, popup);
        }

        public override Vector2 GetWindowSize() => new Vector2(280f, 80f);

        public override void OnGUI(Rect rect) {
            const float pad = 8f;
            const float rowH = 20f;
            const float gap = 4f;

            float x = rect.x + pad;
            float y = rect.y + pad;
            float w = rect.width - pad * 2f;

            // Row 1: type toggle (segmented)
            var kindRect = new Rect(x, y, w, rowH);
            int newKindIdx = GUI.Toolbar(kindRect, (int)kind, new[] { "Klip", "Zeq" });
            if (newKindIdx != (int)kind) {
                // Update suggestion only if the user hasn't edited the name away from the
                // previous suggestion. Keeps custom names sticky across type flips.
                string prevSuggestion = SuggestName(kind);
                kind = (ZoundKind)newKindIdx;
                if (name == prevSuggestion) name = SuggestName(kind);
            }

            y += rowH + gap;

            // Row 2: name input
            var nameRect = new Rect(x, y, w, rowH);
            GUI.SetNextControlName(k_NameFieldCtrl);
            name = EditorGUI.TextField(nameRect, name);
            if (!focusedOnce && Event.current.type == EventType.Repaint) {
                EditorGUI.FocusTextInControl(k_NameFieldCtrl);
                focusedOnce = true;
            }

            y += rowH + gap;

            // Row 3: Create button + Cancel button
            float btnW = 80f;
            var createRect = new Rect(rect.xMax - pad - btnW, y, btnW, rowH);
            var cancelRect = new Rect(createRect.x - gap - btnW, y, btnW, rowH);

            if (GUI.Button(cancelRect, "Cancel")) {
                editorWindow.Close();
                Event.current.Use();
                return;
            }

            bool submit = GUI.Button(createRect, "Create");
            // Enter key submits when the name field has focus.
            if (!submit && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)) {
                submit = true;
                Event.current.Use();
            }
            if (submit) {
                Submit();
            }
        }

        private void Submit() {
            string requestedName = string.IsNullOrWhiteSpace(name) ? SuggestName(kind) : name.Trim();
            // Close the popup first so the Klip file-picker menu doesn't open underneath it.
            editorWindow.Close();

            if (kind == ZoundKind.Zequence) {
                ZoundsWindow.ModifyZoundsProject("add new zequence", () => {
                    var newZequence = new Zequence(ZoundLibrary.GetUniqueZoundId());
                    newZequence.name = ZoundDictionary.EnsureUniqueZoundName(requestedName);
                    BrowserTab.OnZequenceAdded(newZequence);
                }, true);
            }
            else {
                // Klip: delegate to the existing audio-file dialog with the chosen name as override.
                // The dialog's search-text state is tracked inside BrowserTab — we forward its current
                // value to preserve the same behavior as the legacy menu path.
                BrowserTab.OpenCreateNewKlipDialogExternal(triggerMouseGui, requestedName, previewOwner);
            }
        }

        /// <summary>
        /// Builds a unique default name for the given kind. Matches the "New Klip" / "New Zequence"
        /// convention from the legacy menu.
        /// </summary>
        private static string SuggestName(ZoundKind kind) {
            string baseName = kind == ZoundKind.Klip ? "New Klip" : "New Zequence";
            return ZoundDictionary.EnsureUniqueZoundName(baseName);
        }
    }

}
