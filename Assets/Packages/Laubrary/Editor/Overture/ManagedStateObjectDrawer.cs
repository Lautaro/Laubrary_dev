using Laubrary.Overture;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Overture.Editor
{
    /// <summary>
    /// Draws a ManagedStateObject as a GameObject field, a phases multi-select dropdown,
    /// and an Enable/Disable toggle for each selected phase.
    /// Phases not selected are treated as Ignored.
    /// </summary>
    [CustomPropertyDrawer(typeof(ManagedStateObject))]
    public class ManagedStateObjectDrawer : PropertyDrawer
    {
        private static readonly StatePhase[] Phases =
        {
            StatePhase.Entering,
            StatePhase.Entered,
            StatePhase.Exiting,
            StatePhase.Exited,
        };

        private static readonly string[] PhaseLabels = { "Entering", "Entered", "Exiting", "Exited" };

        private const float Spacing    = 2f;
        private const float LabelWidth = 72f;

        private static float RowHeight => EditorGUIUtility.singleLineHeight;

        private int CountControlled(int controlled)
        {
            int count = 0;
            foreach (StatePhase p in Phases)
                if ((controlled & (int)p) != 0) count++;
            return count;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int controlled = property.FindPropertyRelative("controlledPhases").intValue;
            int rows = 2 + CountControlled(controlled); // GO field + phases dropdown + one row per active phase
            return rows * (RowHeight + Spacing);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty gameObjectProp        = property.FindPropertyRelative("gameObject");
            SerializedProperty controlledPhasesProp  = property.FindPropertyRelative("controlledPhases");
            SerializedProperty activePhasesProp      = property.FindPropertyRelative("activePhases");

            float y = position.y;
            float w = position.width;
            float x = position.x;

            // ── GameObject field ─────────────────────────────────────────────
            EditorGUI.PropertyField(new Rect(x, y, w, RowHeight), gameObjectProp, GUIContent.none);
            y += RowHeight + Spacing;

            // ── Phases multi-select dropdown ─────────────────────────────────
            int controlled = controlledPhasesProp.intValue;
            int active     = activePhasesProp.intValue;

            EditorGUI.LabelField(new Rect(x, y, LabelWidth, RowHeight), "Phases");
            StatePhase newControlled = (StatePhase)EditorGUI.EnumFlagsField(
                new Rect(x + LabelWidth, y, w - LabelWidth, RowHeight),
                (StatePhase)controlled);

            // Mask to valid bits only — EnumFlagsField returns -1 for "Everything"
            int newControlledInt = (int)newControlled & 0xF;

            if (newControlledInt != controlled)
            {
                // Clear active bits for phases that were just deselected
                active     &= newControlledInt;
                controlled  = newControlledInt;
                controlledPhasesProp.intValue = controlled;
                activePhasesProp.intValue     = active;
            }

            y += RowHeight + Spacing;

            // ── Enable / Disable row per selected phase ───────────────────────
            for (int i = 0; i < Phases.Length; i++)
            {
                int flag = (int)Phases[i];
                if ((controlled & flag) == 0) continue;

                bool isActive = (active & flag) != 0;

                EditorGUI.LabelField(new Rect(x, y, LabelWidth, RowHeight), PhaseLabels[i]);

                int selection = GUI.Toolbar(
                    new Rect(x + LabelWidth, y, w - LabelWidth, RowHeight),
                    isActive ? 0 : 1,
                    new[] { "Enable", "Disable" });

                bool newActive = selection == 0;
                if (newActive != isActive)
                {
                    if (newActive) active |=  flag;
                    else           active &= ~flag;
                    activePhasesProp.intValue = active;
                }

                y += RowHeight + Spacing;
            }

            EditorGUI.EndProperty();
        }
    }
}
