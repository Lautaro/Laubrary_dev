using UnityEngine;
using UnityEditor;
using System;
using System.Linq;

namespace Laubrary.Lau_StatefulUI.Editor
{
    [CustomPropertyDrawer(typeof(StateModifierFlags))]
    public class StateModifierFlagsDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            bool isInStatefulVisual = property.serializedObject.targetObject is StatefulVisual;
            StateModifierFlags currentValue = (StateModifierFlags)property.intValue;
            
            if (isInStatefulVisual)
            {
                currentValue &= ~StateModifierFlags.GameObjects;
                
                StateModifierFlags newValue = DrawFilteredEnumFlagsField(position, label, currentValue);
                newValue &= ~StateModifierFlags.GameObjects;
                
                if (newValue != (StateModifierFlags)property.intValue)
                {
                    property.intValue = (int)newValue;
                }
            }
            else
            {
                StateModifierFlags newValue = (StateModifierFlags)EditorGUI.EnumFlagsField(position, label, currentValue);
                
                if (newValue != (StateModifierFlags)property.intValue)
                {
                    property.intValue = (int)newValue;
                }
            }
            
            EditorGUI.EndProperty();
        }
        
        private StateModifierFlags DrawFilteredEnumFlagsField(Rect position, GUIContent label, StateModifierFlags currentValue)
        {
            int mask = 0;
            
            var allValues = Enum.GetValues(typeof(StateModifierFlags)).Cast<StateModifierFlags>().ToArray();
            var filteredValues = allValues.Where(v => v != StateModifierFlags.GameObjects && v != StateModifierFlags.None).ToArray();
            var displayNames = filteredValues.Select(v => v.ToString()).ToArray();
            
            for (int i = 0; i < filteredValues.Length; i++)
            {
                if ((currentValue & filteredValues[i]) != 0)
                {
                    mask |= (1 << i);
                }
            }
            
            int newMask = EditorGUI.MaskField(position, label, mask, displayNames);
            
            StateModifierFlags result = StateModifierFlags.None;
            for (int i = 0; i < filteredValues.Length; i++)
            {
                if ((newMask & (1 << i)) != 0)
                {
                    result |= filteredValues[i];
                }
            }
            
            return result;
        }
    }
}
