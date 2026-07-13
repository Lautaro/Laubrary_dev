using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class SavePresetPopup : PopupWindowContent {

        private string name;
        private System.Action<string> onSave;

        public static void Show(Vector2 mousePosition, string currentName, System.Action<string> onSave) {
            var popup = new SavePresetPopup();
            popup.name = currentName;
            popup.onSave = onSave;
            PopupWindow.Show(new Rect(mousePosition, Vector2.zero), popup);
        }

        public static void Show(Rect activatorRect, string currentName, System.Action<string> onSave) {
            var popup = new SavePresetPopup();
            popup.name = currentName;
            popup.onSave = onSave;
            PopupWindow.Show(activatorRect, popup);
        }

        public override Vector2 GetWindowSize() {
            return new Vector2(258, 60);
        }

        public override void OnGUI(Rect rect) {
            float labelWidth = EditorGUIUtility.labelWidth;
            var nameRect = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, EditorGUIUtility.singleLineHeight);

            EditorGUIUtility.labelWidth = 50f;
            GUI.SetNextControlName("name_field");
            name = EditorGUI.TextField(nameRect, "Name", name);
            EditorGUIUtility.labelWidth = labelWidth;
            GUI.FocusControl("name_field");

            var saveRect = new Rect(rect.xMax - 88f, nameRect.yMax + 2, 80f, EditorGUIUtility.singleLineHeight);
            var guiEnabled = GUI.enabled;
            GUI.enabled = name != "Default";
            if (GUI.Button(saveRect, "Save")) {
                editorWindow.Close();
                Save();
            }
            GUI.enabled = guiEnabled;
        }

        private void Save() {
            onSave?.Invoke(name);
            onSave = null;
        }
    }

}
