using UnityEditor;
using UnityEngine;
using Laubrary.Daemon;
using Laubrary.Loom.Editor;

namespace Laubrary.Daemon.Editor
{
    // Daemon authors its Brain in the shared Loom graph window (same canvas as Story, different node palette —
    // the palette auto-lists every BrainNode subclass via the asset's NodeBaseType).
    public class BrainGraphWindow : GraphWindowBase
    {
        protected override string WindowTitle => "Brain Graph";

        public static void Open(Brain b)
        {
            var w = GetWindow<BrainGraphWindow>();
            w.OpenAsset(b);
        }

        [MenuItem("Laubrary/Brain Graph")]
        public static void OpenEmpty()
        {
            var w = GetWindow<BrainGraphWindow>();
            if (Selection.activeObject is Brain b) w.OpenAsset(b);
            else w.Show();
        }
    }

    [CustomEditor(typeof(Brain))]
    public class BrainInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Open in Brain Graph")) BrainGraphWindow.Open((Brain)target);
        }
    }
}
