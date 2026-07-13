using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    [CustomEditor(typeof(ZoundEngine))]
    public class ZoundEngineEditor : Editor {

        [SerializeField] private bool tokensFoldout = true;

        public override void OnInspectorGUI() {
            bool guiEnabled = GUI.enabled;
            GUI.enabled = false;
            base.OnInspectorGUI();
            GUI.enabled = guiEnabled;

            GUILayout.Space(10f);
            if (GUILayout.Button("Stop All Zounds")) {
                ZoundEngine.StopAllZounds(true);
            }
            GUILayout.Space(5f);
            if (GUILayout.Button("Cleanup Unused Sources")) {
                ZoundEngine.Pool.CleanupUnusedSources();
            }

            GUILayout.Space(10f);
            tokensFoldout = EditorGUILayout.Foldout(tokensFoldout, "Debug Tokens", true);
            if (tokensFoldout) {
                bool repaint = false;

                if (ZoundEngine.Instance.hasAnySoloZoundThisFrame) {
                    EditorGUILayout.LabelField("Has Any Solo Zound");
                }

                var labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 20f;
                var cullingGroups = ZoundEngine.CullingGroups;
                foreach (var kvp in cullingGroups) {
                    var zound = kvp.Key;
                    var cullingGroup = kvp.Value;
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(zound.name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField((zound is ClipZound? "Clip" : zound.GetType().Name) + " | CD: " + ZoundEngine.GetRemainingCooldownTime(zound).ToString("0.00") + " s", EditorStyles.miniLabel, GUILayout.Width(100f));
                    GUILayout.EndHorizontal();
                    EditorGUI.indentLevel++;
                    foreach (var token in cullingGroup) {
                        repaint = true;
                        GUILayout.BeginHorizontal(GUILayout.Height(25f));
                        GUI.enabled = token.state == ZoundToken.State.Playing || token.state == ZoundToken.State.Paused;
                        string stateText = "State: " + token.state +
                            " | Time: " + token.time.ToString("0.00") + " / " + token.duration.ToString("0.00");
                        if (token.audioSource.mute) stateText += " | Muted";
                        EditorGUILayout.LabelField(stateText, EditorStyles.miniLabel);
                        if (GUILayout.Button("Fade and Kill", GUILayout.Width(120))) {
                            token.Kill(ZoundsProject.Instance.projectSettings.cullFadeDuration);
                        }
                        GUI.enabled = guiEnabled;
                        GUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUIUtility.labelWidth = labelWidth;
                if (repaint) {
                    Repaint();
                }
            }
        }

    }

}
