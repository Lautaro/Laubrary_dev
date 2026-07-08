using UnityEditor;
using UnityEngine;

namespace Laubrary.Story.Editor
{
    // Inspector for a Story scenario asset: the default fields (metadata + public ruleset + inline LocalRules
    // via the SerializeReference list) plus quick access to its Screenplay graph.
    [CustomEditor(typeof(Story))]
    public class StoryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var story = (Story)target;

            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (story.Screenplay != null)
            {
                if (GUILayout.Button("Open Screenplay in Story Graph", GUILayout.Height(28)))
                    StoryGraphWindow.Open(story.Screenplay);
            }
            else if (GUILayout.Button("Create + attach a Screenplay", GUILayout.Height(28)))
            {
                string path = AssetDatabase.GetAssetPath(story);
                string dir = string.IsNullOrEmpty(path) ? "Assets" : System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                var sp = CreateInstance<Screenplay>();
                string spPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{story.name}_Screenplay.asset");
                AssetDatabase.CreateAsset(sp, spPath);
                story.Screenplay = sp;
                EditorUtility.SetDirty(story);
                AssetDatabase.SaveAssets();
                StoryGraphWindow.Open(sp);
            }

            int local = story.LocalRules != null ? story.LocalRules.Count : 0;
            EditorGUILayout.HelpBox(
                $"Effective ruleset = {(story.PublicRuleSet != null ? story.PublicRuleSet.name : "(no public set)")} " +
                $"+ {local} local rule(s) (local overrides), then merged with the Global ruleset at runtime.",
                MessageType.None);
        }
    }
}
