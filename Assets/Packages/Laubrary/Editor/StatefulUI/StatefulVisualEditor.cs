using UnityEditor;
using UnityEngine;

namespace Laubrary.Lau_StatefulUI.Editor
{
    [CustomEditor(typeof(StatefulVisual))]
    public class StatefulVisualEditor : UnityEditor.Editor
    {
        private SerializedProperty _respondsToDimension;
        private SerializedProperty _interactionOverrides;
        private SerializedProperty _customNormalConfig;
        private SerializedProperty _customHoverConfig;
        private SerializedProperty _customSelectedConfig;
        private SerializedProperty _customClickedConfig;
        private SerializedProperty _customDisabledConfig;
        private SerializedProperty _booleanOverrides;
        private SerializedProperty _customNavigatedConfig;
        private SerializedProperty _customToggledConfig;
        private SerializedProperty _customFocusedConfig;

        private void OnEnable()
        {
            _respondsToDimension = serializedObject.FindProperty("respondsToDimension");
            _interactionOverrides = serializedObject.FindProperty("interactionOverrides");
            _customNormalConfig = serializedObject.FindProperty("customNormalConfig");
            _customHoverConfig = serializedObject.FindProperty("customHoverConfig");
            _customSelectedConfig = serializedObject.FindProperty("customSelectedConfig");
            _customClickedConfig = serializedObject.FindProperty("customClickedConfig");
            _customDisabledConfig = serializedObject.FindProperty("customDisabledConfig");
            _booleanOverrides = serializedObject.FindProperty("booleanOverrides");
            _customNavigatedConfig = serializedObject.FindProperty("customNavigatedConfig");
            _customToggledConfig = serializedObject.FindProperty("customToggledConfig");
            _customFocusedConfig = serializedObject.FindProperty("customFocusedConfig");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_respondsToDimension, new GUIContent("Responds To Dimension"));
            EditorGUILayout.Space(10);

            StateDimension dimension = (StateDimension)_respondsToDimension.enumValueIndex;

            switch (dimension)
            {
                case StateDimension.Interaction:
                    DrawInteractionOverrides();
                    break;
                case StateDimension.Navigated:
                    DrawNavigatedOverrides();
                    break;
                case StateDimension.Toggled:
                    DrawToggledOverrides();
                    break;
                case StateDimension.Focused:
                    DrawFocusedOverrides();
                    break;
            }

            EditorGUILayout.Space(10);
            DrawUtilityButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInteractionOverrides()
        {
            EditorGUI.BeginChangeCheck();
            InteractionStateOverrides oldFlags = (InteractionStateOverrides)_interactionOverrides.intValue;
            EditorGUILayout.PropertyField(_interactionOverrides, new GUIContent("Override States"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                InteractionStateOverrides newFlags = (InteractionStateOverrides)_interactionOverrides.intValue;
                SetCustomFlagForNewOverrides(oldFlags, newFlags, _customNormalConfig, InteractionStateOverrides.Normal);
                SetCustomFlagForNewOverrides(oldFlags, newFlags, _customHoverConfig, InteractionStateOverrides.Hover);
                SetCustomFlagForNewOverrides(oldFlags, newFlags, _customSelectedConfig, InteractionStateOverrides.Selected);
                SetCustomFlagForNewOverrides(oldFlags, newFlags, _customClickedConfig, InteractionStateOverrides.Clicked);
                SetCustomFlagForNewOverrides(oldFlags, newFlags, _customDisabledConfig, InteractionStateOverrides.Disabled);
                serializedObject.ApplyModifiedProperties();
            }

            InteractionStateOverrides flags = (InteractionStateOverrides)_interactionOverrides.intValue;

            if ((flags & InteractionStateOverrides.Normal) != 0)
            {
                DrawInteractionConfig("Normal", _customNormalConfig);
            }
            if ((flags & InteractionStateOverrides.Hover) != 0)
            {
                DrawInteractionConfig("Hover", _customHoverConfig);
            }
            if ((flags & InteractionStateOverrides.Selected) != 0)
            {
                DrawInteractionConfig("Selected", _customSelectedConfig);
            }
            if ((flags & InteractionStateOverrides.Clicked) != 0)
            {
                DrawInteractionConfig("Clicked", _customClickedConfig);
            }
            if ((flags & InteractionStateOverrides.Disabled) != 0)
            {
                DrawInteractionConfig("Disabled", _customDisabledConfig);
            }
        }

        private void DrawInteractionConfig(string label, SerializedProperty config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            SerializedProperty custom = config.FindPropertyRelative("custom");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Custom", GUILayout.Width(50));
            custom.boolValue = EditorGUILayout.Toggle(custom.boolValue, GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
            
            if (custom.boolValue)
            {
                DrawInteractionConfigFields(config);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void DrawInteractionConfigFields(SerializedProperty config)
        {
            SerializedProperty modifiers = config.FindPropertyRelative("enabledModifiers");
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(modifiers, new GUIContent("Modifiers"), true);
            if (EditorGUI.EndChangeCheck())
            {
                StateModifierFlags modifierFlags = (StateModifierFlags)modifiers.intValue;
                modifierFlags &= ~StateModifierFlags.GameObjects;
                modifiers.intValue = (int)modifierFlags;
                
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            StateModifierFlags flags = (StateModifierFlags)modifiers.intValue;

            if ((flags & StateModifierFlags.Color) != 0)
            {
                EditorGUILayout.PropertyField(config.FindPropertyRelative("color"));
            }
            if ((flags & StateModifierFlags.Alpha) != 0)
            {
                DrawModifierWithMode(config, "alpha", "alphaIsAdditive", "Alpha");
            }
            if ((flags & StateModifierFlags.ColorValue) != 0)
            {
                DrawModifierWithMode(config, "colorValue", "colorValueIsAdditive", "Color Value");
            }
            if ((flags & StateModifierFlags.Saturation) != 0)
            {
                DrawModifierWithMode(config, "saturation", "saturationIsAdditive", "Saturation");
            }
            if ((flags & StateModifierFlags.Position) != 0)
            {
                SerializedProperty posProp = config.FindPropertyRelative("position");
                EditorGUILayout.BeginHorizontal();
                posProp.vector2Value = EditorGUILayout.Vector2Field("Position", posProp.vector2Value);
                EditorGUILayout.LabelField("Add", GUILayout.Width(30));
                EditorGUILayout.PropertyField(config.FindPropertyRelative("positionIsAdditive"), GUIContent.none, GUILayout.Width(15));
                EditorGUILayout.EndHorizontal();
            }
            if ((flags & StateModifierFlags.Scale) != 0)
            {
                DrawModifierWithMode(config, "scale", "scaleIsAdditive", "Scale");
            }
            if ((flags & StateModifierFlags.Rotation) != 0)
            {
                DrawRotationModifier(config);
            }
            if ((flags & StateModifierFlags.Sprite) != 0)
            {
                EditorGUILayout.PropertyField(config.FindPropertyRelative("sprite"));
            }
        }

        private void DrawModifierWithMode(SerializedProperty config, string valueName, string modeName, string label)
        {
            EditorGUILayout.BeginHorizontal();
            
            SerializedProperty valueProp = config.FindPropertyRelative(valueName);
            SerializedProperty isAdditive = config.FindPropertyRelative(modeName);
            
            float minValue = isAdditive.boolValue ? -1f : 0f;
            float maxValue = 1f;
            
            valueProp.floatValue = EditorGUILayout.Slider(label, valueProp.floatValue, minValue, maxValue);
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Add", GUILayout.Width(30));
            
            EditorGUI.BeginChangeCheck();
            bool newAdditive = EditorGUILayout.Toggle(isAdditive.boolValue, GUILayout.Width(15));
            if (EditorGUI.EndChangeCheck())
            {
                isAdditive.boolValue = newAdditive;
                if (newAdditive && valueProp.floatValue < -1f)
                {
                    valueProp.floatValue = -1f;
                }
                else if (!newAdditive && valueProp.floatValue < 0f)
                {
                    valueProp.floatValue = 0f;
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRotationModifier(SerializedProperty config)
        {
            SerializedProperty rotationProp = config.FindPropertyRelative("rotation");
            rotationProp.floatValue = EditorGUILayout.Slider("Rotation", rotationProp.floatValue, -360f, 360f);
        }

        private void DrawNavigatedOverrides()
        {
            EditorGUI.BeginChangeCheck();
            BooleanStateOverrides oldFlags = (BooleanStateOverrides)_booleanOverrides.intValue;
            
            bool isNavigatedEnabled = (oldFlags & BooleanStateOverrides.Navigated) != 0;
            bool newNavigatedEnabled = EditorGUILayout.Toggle("Override Navigated", isNavigatedEnabled);
            
            if (EditorGUI.EndChangeCheck())
            {
                BooleanStateOverrides newFlags = oldFlags;
                
                if (newNavigatedEnabled)
                {
                    newFlags |= BooleanStateOverrides.Navigated;
                }
                else
                {
                    newFlags &= ~BooleanStateOverrides.Navigated;
                }
                
                newFlags &= ~BooleanStateOverrides.Toggled;
                newFlags &= ~BooleanStateOverrides.Focused;
                
                _booleanOverrides.intValue = (int)newFlags;
                serializedObject.ApplyModifiedProperties();
                
                if (!isNavigatedEnabled && newNavigatedEnabled)
                {
                    SetCustomFlagForNewOverrides(oldFlags, newFlags, _customNavigatedConfig, BooleanStateOverrides.Navigated);
                    serializedObject.ApplyModifiedProperties();
                }
            }

            BooleanStateOverrides flags = (BooleanStateOverrides)_booleanOverrides.intValue;

            if ((flags & BooleanStateOverrides.Navigated) != 0)
            {
                DrawBooleanConfig("Navigated", _customNavigatedConfig);
            }
        }

        private void DrawToggledOverrides()
        {
            EditorGUI.BeginChangeCheck();
            BooleanStateOverrides oldFlags = (BooleanStateOverrides)_booleanOverrides.intValue;
            
            bool isToggledEnabled = (oldFlags & BooleanStateOverrides.Toggled) != 0;
            bool newToggledEnabled = EditorGUILayout.Toggle("Override Toggled", isToggledEnabled);
            
            if (EditorGUI.EndChangeCheck())
            {
                BooleanStateOverrides newFlags = oldFlags;
                
                if (newToggledEnabled)
                {
                    newFlags |= BooleanStateOverrides.Toggled;
                }
                else
                {
                    newFlags &= ~BooleanStateOverrides.Toggled;
                }
                
                newFlags &= ~BooleanStateOverrides.Navigated;
                newFlags &= ~BooleanStateOverrides.Focused;
                
                _booleanOverrides.intValue = (int)newFlags;
                serializedObject.ApplyModifiedProperties();
                
                if (!isToggledEnabled && newToggledEnabled)
                {
                    SetCustomFlagForNewOverrides(oldFlags, newFlags, _customToggledConfig, BooleanStateOverrides.Toggled);
                    serializedObject.ApplyModifiedProperties();
                }
            }

            BooleanStateOverrides flags = (BooleanStateOverrides)_booleanOverrides.intValue;

            if ((flags & BooleanStateOverrides.Toggled) != 0)
            {
                DrawBooleanConfig("Toggled", _customToggledConfig);
            }
        }

        private void DrawFocusedOverrides()
        {
            EditorGUI.BeginChangeCheck();
            BooleanStateOverrides oldFlags = (BooleanStateOverrides)_booleanOverrides.intValue;
            
            bool isFocusedEnabled = (oldFlags & BooleanStateOverrides.Focused) != 0;
            bool newFocusedEnabled = EditorGUILayout.Toggle("Override Focused", isFocusedEnabled);
            
            if (EditorGUI.EndChangeCheck())
            {
                BooleanStateOverrides newFlags = oldFlags;
                
                if (newFocusedEnabled)
                {
                    newFlags |= BooleanStateOverrides.Focused;
                }
                else
                {
                    newFlags &= ~BooleanStateOverrides.Focused;
                }
                
                newFlags &= ~BooleanStateOverrides.Navigated;
                newFlags &= ~BooleanStateOverrides.Toggled;
                
                _booleanOverrides.intValue = (int)newFlags;
                serializedObject.ApplyModifiedProperties();
                
                if (!isFocusedEnabled && newFocusedEnabled)
                {
                    SetCustomFlagForNewOverrides(oldFlags, newFlags, _customFocusedConfig, BooleanStateOverrides.Focused);
                    serializedObject.ApplyModifiedProperties();
                }
            }

            BooleanStateOverrides flags = (BooleanStateOverrides)_booleanOverrides.intValue;

            if ((flags & BooleanStateOverrides.Focused) != 0)
            {
                DrawBooleanConfig("Focused", _customFocusedConfig);
            }
        }

        private void DrawBooleanConfig(string label, SerializedProperty config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            SerializedProperty custom = config.FindPropertyRelative("custom");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Custom", GUILayout.Width(50));
            custom.boolValue = EditorGUILayout.Toggle(custom.boolValue, GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
            
            if (custom.boolValue)
            {
                DrawBooleanConfigFields(config);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void DrawBooleanConfigFields(SerializedProperty config)
        {
            SerializedProperty modifiers = config.FindPropertyRelative("enabledModifiers");
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(modifiers, new GUIContent("Modifiers"), true);
            if (EditorGUI.EndChangeCheck())
            {
                StateModifierFlags modifierFlags = (StateModifierFlags)modifiers.intValue;
                modifierFlags &= ~StateModifierFlags.GameObjects;
                modifiers.intValue = (int)modifierFlags;
                
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            StateModifierFlags flags = (StateModifierFlags)modifiers.intValue;

            if ((flags & StateModifierFlags.Alpha) != 0)
            {
                DrawModifierWithMode(config, "alpha", "alphaIsAdditive", "Alpha");
            }
            if ((flags & StateModifierFlags.ColorValue) != 0)
            {
                DrawModifierWithMode(config, "colorValue", "colorValueIsAdditive", "Color Value");
            }
            if ((flags & StateModifierFlags.Saturation) != 0)
            {
                DrawModifierWithMode(config, "saturation", "saturationIsAdditive", "Saturation");
            }
            if ((flags & StateModifierFlags.Position) != 0)
            {
                SerializedProperty posProp = config.FindPropertyRelative("position");
                EditorGUILayout.BeginHorizontal();
                posProp.vector2Value = EditorGUILayout.Vector2Field("Position", posProp.vector2Value);
                EditorGUILayout.LabelField("Add", GUILayout.Width(30));
                EditorGUILayout.PropertyField(config.FindPropertyRelative("positionIsAdditive"), GUIContent.none, GUILayout.Width(15));
                EditorGUILayout.EndHorizontal();
            }
            if ((flags & StateModifierFlags.Scale) != 0)
            {
                DrawModifierWithMode(config, "scale", "scaleIsAdditive", "Scale");
            }
            if ((flags & StateModifierFlags.Rotation) != 0)
            {
                DrawRotationModifier(config);
            }
            if ((flags & StateModifierFlags.Sprite) != 0)
            {
                EditorGUILayout.PropertyField(config.FindPropertyRelative("sprite"));
            }
        }

        private void DrawUtilityButtons()
        {
            if (GUILayout.Button("Revert All to Default"))
            {
                if (EditorUtility.DisplayDialog("Revert All Overrides",
                    "Are you sure you want to revert all state overrides to use parent defaults?",
                    "Yes", "Cancel"))
                {
                    StatefulVisual visual = (StatefulVisual)target;
                    visual.ResetToDefaults();
                    EditorUtility.SetDirty(visual);
                }
            }
        }

        private void SetCustomFlagForNewOverrides<T>(T oldFlags, T newFlags, SerializedProperty config, T flag) where T : System.Enum
        {
            int oldValue = System.Convert.ToInt32(oldFlags);
            int newValue = System.Convert.ToInt32(newFlags);
            int flagValue = System.Convert.ToInt32(flag);
            
            bool wasEnabled = (oldValue & flagValue) != 0;
            bool isEnabled = (newValue & flagValue) != 0;
            
            if (!wasEnabled && isEnabled)
            {
                SerializedProperty customProp = config.FindPropertyRelative("custom");
                if (customProp != null)
                {
                    customProp.boolValue = true;
                }
            }
        }
    }
}
