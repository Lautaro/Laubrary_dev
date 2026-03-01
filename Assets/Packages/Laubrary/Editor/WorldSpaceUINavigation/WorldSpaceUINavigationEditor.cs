using UnityEditor;
using UnityEngine;
using Laubrary.WorldSpaceUINavigation;

namespace Laubrary.WorldSpaceUINavigation.Editor
{
    [CustomEditor(typeof(WorldSpaceUINavigation))]
    public class WorldSpaceUINavigationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }
}
