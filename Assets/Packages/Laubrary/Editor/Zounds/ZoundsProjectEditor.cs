using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    [CustomEditor(typeof(ZoundsProject))]
    public class ZoundsProjectEditor : Editor {

        public override void OnInspectorGUI() {
            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            {
                GUILayout.Space(10f);
                if (GUILayout.Button("Open Zounds Window")) {
                    ZoundsWindow.OpenWindow();
                }
                GUILayout.Space(10f);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);

            // Inspector is locked — the JSON file is the source of truth.
            // Editing the ScriptableObject directly will be overwritten on next load.
            bool prevGUIEnabled = GUI.enabled;
            GUI.enabled = false;
            base.OnInspectorGUI();
            GUI.enabled = prevGUIEnabled;
        }

    }

}