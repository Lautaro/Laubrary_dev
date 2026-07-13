using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {
    public class InfoViewWindow : BaseZoundEditorWindow<Zound, InfoViewWindow> {

        [SerializeField] private bool showCullingGroup = true;
        [SerializeField] private bool showTags = true;
        [SerializeField] private bool showReferences = true;

        private ZoundLibrary.Tag[] tagsCache = null;

        public static InfoViewWindow OpenWindow(Zound zound) {
            return OpenWindow<InfoViewWindow>(zound, new Vector2(200f, 150f));
        }

        protected override void OnInit() {
            base.OnInit();
            titleContent.text = "Info: " + (targetZound == null ? "(Invalid)" : targetZound.name);
        }

        protected override Zound FindZoundTarget() {
            var library = ZoundsProject.Instance.zoundLibrary;
            Zound target = library.FindZound(z => z.id == targetZoundID);
            return target;
        }

        protected override bool OnDrawGUI() {
            GUILayout.BeginHorizontal();
            GUILayout.Space(17f);
            string cd = ZoundEngine.GetRemainingCooldownTime(targetZound).ToString("0.00") + " s";
            EditorGUILayout.LabelField("Cooldown: " + cd, EditorStyles.boldLabel);
            GUILayout.EndHorizontal();

            ZoundEngine.CullingGroups.TryGetValue(targetZound, out var cullingGroup);
            bool temp = EditorGUILayout.BeginFoldoutHeaderGroup(showCullingGroup, "Culling Group: " + (cullingGroup == null ? 0 : cullingGroup.Count));
            //bool temp = EditorGUILayout.Foldout(, true);
            if (temp != showCullingGroup) {
                Undo.RecordObject(this, "toggle culling group");
                showCullingGroup = temp;
                EditorUtility.SetDirty(this);
            }
            if (showCullingGroup && cullingGroup != null) {
                bool guiEnabled = GUI.enabled;
                EditorGUI.indentLevel++;
                foreach (var token in cullingGroup) {
                    GUILayout.BeginHorizontal(GUILayout.Height(25f));
                    GUI.enabled = token.state == ZoundToken.State.Playing || token.state == ZoundToken.State.Paused;
                    EditorGUILayout.LabelField(
                        "State: " + token.state +
                        " | Time: " + token.time.ToString("0.00") + " / " + token.duration.ToString("0.00"),
                        EditorStyles.miniLabel);
                    if (GUILayout.Button("Fade and Kill", GUILayout.Width(120))) {
                        token.Kill(ZoundsProject.Instance.projectSettings.cullFadeDuration);
                    }
                    GUI.enabled = guiEnabled;
                    GUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            TagsEditorWindow.CleanupUnregisteredTags(targetZound);
            temp = EditorGUILayout.BeginFoldoutHeaderGroup(showTags, "Tags: " + targetZound.tags.Count);
            if (temp != showTags) {
                Undo.RecordObject(this, "toggle tags");
                showTags = temp;
                EditorUtility.SetDirty(this);
            }
            if (showTags) {
                bool refreshCache = tagsCache == null;
                if (!refreshCache) refreshCache = targetZound.tags.Count != tagsCache.Length;
                if (!refreshCache) {
                    for (int i = 0; i < tagsCache.Length; i++) {
                        if (targetZound.tags[i] != tagsCache[i].id) {
                            refreshCache = true; break;
                        }
                    }
                }

                if (refreshCache) {
                    var zoundsProject = ZoundsProject.Instance;
                    var zoundLibrary = zoundsProject.zoundLibrary;
                    var projectTags = zoundLibrary.tags;
                    tagsCache = projectTags.Where(tag => targetZound.tags.Contains(tag.id)).ToArray();
                }

                EditorGUI.indentLevel++;
                var col = GUI.color;
                GUI.color = Color.yellow;
                for (int i = 0; i < tagsCache.Length; i++) {
                    EditorGUILayout.LabelField(tagsCache[i].name);
                }
                GUI.color = col;
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            temp = EditorGUILayout.BeginFoldoutHeaderGroup(showReferences, "References");
            if (temp != showReferences) {
                Undo.RecordObject(this, "toggle references");
                showReferences = temp;
                EditorUtility.SetDirty(this);
            }
            if (showReferences) {
                List<Zound> directReferences = AudioAssetUtility.GetDirectZoundReferences(targetZound);
                List<Zound> nestedReferences = AudioAssetUtility.GetNestedZoundReferences(targetZound);
                List<Zound> dependencies = targetZound.GetDependencies();
                EditorGUI.indentLevel+=2;
                if (directReferences.Count > 0) {
                    EditorGUILayout.LabelField("Direct:", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    foreach (Zound reference in directReferences) {
                        EditorGUILayout.LabelField(string.Format("{0}  ({1})", reference.name, reference.GetType().Name));
                    }
                    EditorGUI.indentLevel--;
                }
                if (nestedReferences.Count > 0) {
                    EditorGUILayout.LabelField("Nested:", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    foreach (Zound reference in nestedReferences) {
                        EditorGUILayout.LabelField(string.Format("{0}  ({1})", reference.name, reference.GetType().Name));
                    }
                    EditorGUI.indentLevel--;
                }
                if (dependencies.Count > 0) {
                    EditorGUILayout.LabelField("Dependencies:", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    foreach (Zound dependency in dependencies) {
                        EditorGUILayout.LabelField(string.Format("{0}  ({1})", dependency.name, dependency.GetType().Name));
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel-=2;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            return false;
        }

    }
}
