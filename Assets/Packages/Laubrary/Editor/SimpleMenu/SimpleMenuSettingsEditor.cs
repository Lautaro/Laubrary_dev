using UnityEngine;
using UnityEditor;
using Laubrary.SimpleMenu;

namespace Laubrary.SimpleMenu.Editor
{
    [CustomEditor(typeof(SimpleMenuSettings))]
    public class SimpleMenuSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SimpleMenuSettings settings = (SimpleMenuSettings)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Theme Management", EditorStyles.boldLabel);

            if (GUILayout.Button("Apply to All Menus in Scene"))
            {
                ApplyToAllInScene(settings);
            }
        }

        private void ApplyToAllInScene(SimpleMenuSettings settings)
        {
            var menus = GameObject.FindObjectsByType<SimpleMenuBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;

            foreach (var menu in menus)
            {
                // Assign to the component so it's persisted in the scene
                menu.menuSettings = settings;
                EditorUtility.SetDirty(menu);
                count++;
            }

            // Also update the static default so any menus created at runtime use this
            SimpleMenuBase.GlobalDefaultSettings = settings;

            Debug.Log($"Applied {settings.name} to {count} menus in the current scene.");
        }
    }
}
