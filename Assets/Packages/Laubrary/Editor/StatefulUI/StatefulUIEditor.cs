using UnityEditor;
using UnityEngine;
using Laubrary.Lau_StatefulUI;

namespace Laubrary.Lau_StatefulUI.Editor
{
    [CustomEditor(typeof(StatefulUI))]
    public class StatefulUIEditor : UnityEditor.Editor
    {
        private SerializedProperty _normalConfig;
        private SerializedProperty _hoverConfig;
        private SerializedProperty _selectedConfig;
        private SerializedProperty _setSelectedOnClick;
        private SerializedProperty _clickedConfig;
        private SerializedProperty _disabledConfig;
        private SerializedProperty _navigatedConfig;
        private SerializedProperty _toggledConfig;
        private SerializedProperty _setToggledOnClick;
        private SerializedProperty _focusedConfig;
        private SerializedProperty _navigatedFalseConfig;
        private SerializedProperty _toggledOnConfig;
        private SerializedProperty _toggledOffConfig;
        private SerializedProperty _focusedTrueConfig;
        private SerializedProperty _focusedFalseConfig;
        private SerializedProperty _radioGroupId;
        private SerializedProperty _groupBehavior;
        private SerializedProperty _applyVisualToSelf;
        private SerializedProperty _transitionDuration;

        private bool _showInteractionStates = true;
        private bool _showBooleanDimensions = true;
        private bool _showRadioGroup = true;
        private bool _showSelfVisual = true;
        private bool _showTransitionSettings = true;
        private bool _showCallbacks = false;

        private void OnEnable()
        {
            _normalConfig = serializedObject.FindProperty("normalConfig");
            _hoverConfig = serializedObject.FindProperty("hoverConfig");
            _selectedConfig = serializedObject.FindProperty("selectedConfig");
            _setSelectedOnClick = serializedObject.FindProperty("setSelectedOnClick");
            _clickedConfig = serializedObject.FindProperty("clickedConfig");
            _disabledConfig = serializedObject.FindProperty("disabledConfig");
            _navigatedConfig = serializedObject.FindProperty("navigatedConfig");
            _toggledConfig = serializedObject.FindProperty("toggledConfig");
            _setToggledOnClick = serializedObject.FindProperty("setToggledOnClick");
            _focusedConfig = serializedObject.FindProperty("focusedConfig");
            _radioGroupId = serializedObject.FindProperty("radioGroupId");
            _groupBehavior = serializedObject.FindProperty("groupBehavior");
            _applyVisualToSelf = serializedObject.FindProperty("applyVisualToSelf");
            _transitionDuration = serializedObject.FindProperty("transitionDuration");
        }

        public override void OnInspectorGUI()
        {
            var stateItemSpace = 5;
            serializedObject.Update();

            DrawCurrentStateLabel();
            DrawSelfVisual(); DrawTransitionSettings();
            EditorGUILayout.Space(stateItemSpace);
            _showInteractionStates = EditorGUILayout.BeginFoldoutHeaderGroup(_showInteractionStates, "Interaction States");
            if (_showInteractionStates)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawInteractionStates();
                DrawBooleanDimensions();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space(stateItemSpace);
            DrawRadioGroup();
            EditorGUILayout.Space(stateItemSpace);
            DrawEvents();
            EditorGUILayout.Space(stateItemSpace);
            DrawUtilityButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInteractionStates()
        {
            DrawInteractionStateConfig("Normal", _normalConfig);
            DrawInteractionStateConfig("Hover", _hoverConfig);
            DrawInteractionStateConfig("Selected", _selectedConfig);

            DrawInteractionStateConfig("Clicked", _clickedConfig);
            DrawInteractionStateConfig("Disabled", _disabledConfig);
        }

        private void DrawInteractionStateConfig(string label, SerializedProperty config)
        {
            SerializedProperty custom = config.FindPropertyRelative("custom");


            EditorGUILayout.PropertyField(custom, new GUIContent(label));

            if (custom.boolValue)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                SerializedProperty modifiers = config.FindPropertyRelative("enabledModifiers");

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(modifiers, new GUIContent("Modifiers"), true);
                if (EditorGUI.EndChangeCheck())
                {
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
                if ((flags & StateModifierFlags.GameObjects) != 0)
                {
                    DrawGameObjectsList(config.FindPropertyRelative("gameObjects"));
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);

            }

            if (custom.boolValue && label == "Selected")
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_setSelectedOnClick, new GUIContent("Set On Click"));
                EditorGUI.indentLevel--;
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

        private void DrawBooleanDimensions()
        {
            if (_showBooleanDimensions)
            {
                DrawBooleanStateConfig("Navigated", _navigatedConfig);
                DrawBooleanStateConfig("Toggled", _toggledConfig);
                if (_toggledConfig.FindPropertyRelative("custom").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_setToggledOnClick, new GUIContent("Set On Click"));
                    EditorGUI.indentLevel--;
                }
                DrawBooleanStateConfig("Focused", _focusedConfig);
            }
        }

        private void DrawBooleanStateConfig(string label, SerializedProperty config)
        {
            SerializedProperty custom = config.FindPropertyRelative("custom");

            EditorGUILayout.PropertyField(custom, new GUIContent(label));

            if (custom.boolValue)
            {
                SerializedProperty modifiers = config.FindPropertyRelative("enabledModifiers");

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(modifiers, new GUIContent("Modifiers"), true);
                if (EditorGUI.EndChangeCheck())
                {
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
                if ((flags & StateModifierFlags.GameObjects) != 0)
                {
                    DrawGameObjectsList(config.FindPropertyRelative("gameObjects"));
                }
            }
        }

        private void DrawRadioGroup()
        {
            _showRadioGroup = EditorGUILayout.BeginFoldoutHeaderGroup(_showRadioGroup, "Radio Group");
            if (_showRadioGroup)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_radioGroupId, new GUIContent("Group ID"));
                EditorGUILayout.PropertyField(_groupBehavior, new GUIContent("Behavior"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSelfVisual()
        {
            if (_showSelfVisual)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_applyVisualToSelf, new GUIContent("Apply on Self"));
                EditorGUI.indentLevel--;
            }
        }

        private void DrawTransitionSettings()
        {
            if (_showTransitionSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_transitionDuration, new GUIContent("Transition Duration"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawCurrentStateLabel()
        {
            if (Application.isPlaying)
            {
                StatefulUI statefulUI = (StatefulUI)target;
                string stateText = $"State: {statefulUI.CurrentInteractionState}";

                if (statefulUI.IsSelected) stateText += " | Selected";
                if (statefulUI.IsNavigated) stateText += " | Navigated";
                if (statefulUI.IsToggled) stateText += " | Toggled";
                if (statefulUI.IsFocused) stateText += " | Focused";

                EditorGUILayout.HelpBox(stateText, MessageType.None);

                Repaint();
            }
        }

        private void DrawEvents()
        {
            _showCallbacks = EditorGUILayout.BeginFoldoutHeaderGroup(_showCallbacks, "Callbacks");
            if (_showCallbacks)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnInteractionStateChanged"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnNavigatedChanged"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnToggledChanged"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnFocusedChanged"));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawGameObjectsList(SerializedProperty listProperty)
        {
            EditorGUI.indentLevel++;

            int size = listProperty.arraySize;
            int newSize = EditorGUILayout.IntField("Size", size);

            if (newSize != size)
            {
                listProperty.arraySize = newSize;
            }

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                SerializedProperty element = listProperty.GetArrayElementAtIndex(i);
                SerializedProperty gameObjectProp = element.FindPropertyRelative("gameObject");
                SerializedProperty enabledProp = element.FindPropertyRelative("enabled");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(gameObjectProp, GUIContent.none, GUILayout.MaxWidth(180));
                EditorGUILayout.LabelField("Enabled", GUILayout.Width(60));
                EditorGUILayout.PropertyField(enabledProp, GUIContent.none, GUILayout.Width(50));

                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    listProperty.DeleteArrayElementAtIndex(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add GameObject"))
            {
                listProperty.arraySize++;
                SerializedProperty newElement = listProperty.GetArrayElementAtIndex(listProperty.arraySize - 1);
                newElement.FindPropertyRelative("gameObject").objectReferenceValue = null;
                newElement.FindPropertyRelative("enabled").boolValue = true;
            }

            EditorGUI.indentLevel--;
        }

        private void DrawUtilityButtons()
        {
            if (GUILayout.Button("Force Settings to All Children"))
            {
                StatefulUI statefulUI = (StatefulUI)target;
                statefulUI.ForceSettingsToAllChildren();
                EditorUtility.SetDirty(statefulUI);
            }

            if (GUILayout.Button("Refresh Child Visuals"))
            {
                StatefulUI statefulUI = (StatefulUI)target;
                statefulUI.RefreshChildVisuals();
            }
        }
    }
}
