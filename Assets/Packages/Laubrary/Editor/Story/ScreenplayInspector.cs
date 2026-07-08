using UnityEditor;
using UnityEngine;
using Laubrary.Story;

namespace Laubrary.Story.Editor
{
    // Standard inspector for a Screenplay (the [SerializeReference] Pages list is editable here) plus a button
    // to open the visual graph for wiring topology.
    [CustomEditor(typeof(Screenplay))]
    public class ScreenplayInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var sp = (Screenplay)target;

            EditorGUILayout.HelpBox(
                $"{sp.Pages?.Count ?? 0} page(s), {sp.Edges?.Count ?? 0} edge(s). Entry: " +
                (string.IsNullOrEmpty(sp.EntryId) ? "(auto)" : sp.GetPage(sp.EntryId)?.Title ?? sp.EntryId),
                MessageType.None);

            if (GUILayout.Button("Open in Story Graph", GUILayout.Height(28)))
                StoryGraphWindow.Open(sp);

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
