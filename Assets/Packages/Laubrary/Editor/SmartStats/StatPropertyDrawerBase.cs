//using Lautaro.Stats;
//using Lautaro.Stats.Engine;
//using System.Collections.Generic;
//using System.Reflection;
//using UnityEditor;
//using UnityEngine;

//namespace StatEditor {

//    public class StatPropertyDrawerBase : PropertyDrawer {

//        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
//            float lineHeight = EditorGUIUtility.singleLineHeight + 1;
//            var height = lineHeight;

//            var stat = GetTargetObjectOfProperty(property) as StatBase<object>;
//            if (stat != null) {
//                var modifiers = GetModifiers(stat);
//                if (modifiers == null || modifiers.Count == 0) {
//                    return height;
//                }
//                if (property.isExpanded) {
//                    height += lineHeight;
//                    if (modifiers != null) {
//                        for (int i = 0; i < modifiers.Count; i++) {
//                            height += lineHeight;
//                        }
//                    }
//                }
//                height += EditorGUIUtility.standardVerticalSpacing;
//            }

//            return height;
//        }

//        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
//            var stat = GetTargetObjectOfProperty(property) as StatBase<object>;
//            if (stat != null) {
//                var modifiers = GetModifiers(stat);
//                var indent = EditorGUI.indentLevel;
//                EditorGUI.indentLevel = 0;
//                label = EditorGUI.BeginProperty(position, label, property);


//                var lineHeight = EditorGUIUtility.singleLineHeight + 1;
//                var currentRect = new Rect(position.x, position.y, position.width, lineHeight);


//                if (modifiers != null && modifiers.Count > 0) {
//                    currentRect = DrawFoldout(property, label, currentRect, stat);
//                    if (property.isExpanded) {
//                        EditorGUI.indentLevel++;
//                        DrawExpandedProperty(property, lineHeight, currentRect, modifiers);
//                        EditorGUI.indentLevel--;
//                    }
//                }
//                else {
//                    DrawOneLineProperty(property, label, currentRect);
//                }

//                if (Application.isPlaying) {
//                    GUI.changed = true;
//                }

//                EditorGUI.EndProperty();
//                EditorGUI.indentLevel = indent;
//            }
//        }

//        private Rect DrawOneLineProperty(SerializedProperty property, GUIContent label, Rect currentRect) {
//            var baseValue = property.FindPropertyRelative("baseValue");
//            EditorGUI.PropertyField(currentRect, baseValue, label);
//            return currentRect;
//        }

//        private Rect DrawFoldout(SerializedProperty property, GUIContent label, Rect currentRect, StatBase<object> stat) {
//            var labelWidth = EditorGUIUtility.labelWidth;
//            var labelRect = new Rect(currentRect.x, currentRect.y, labelWidth, currentRect.height);
//            var valueRect = new Rect(currentRect.x + labelWidth, currentRect.y, currentRect.width - labelWidth, currentRect.height);
//            valueRect.x += 3.5f;
//            valueRect.width -= 3.5f;
//            property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, label, true);
//            OnDrawValue(stat, valueRect);
//            return currentRect;
//        }

//        protected virtual void OnDrawValue(StatBase<object> stat, Rect valueRect) {
            
//        }

//        private void DrawExpandedProperty(SerializedProperty property, float lineHeight, Rect currentRect, List<StatModifierBase<object>> modifiers) {
//            currentRect.y += lineHeight;
//            var labelWidth = EditorGUIUtility.labelWidth;
//            var correctedLabelWidth = labelWidth + 1.5f;

//            var baseValue = property.FindPropertyRelative("baseValue");
//            EditorGUIUtility.labelWidth = correctedLabelWidth;
//            EditorGUI.PropertyField(currentRect, baseValue);
//            EditorGUIUtility.labelWidth = labelWidth;

//            if (modifiers != null) {
//                foreach (var modifier in modifiers) {
//                    currentRect.y += lineHeight;
//                    var col = GUI.color;
//                    GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
//                    var lineRect = currentRect;
//                    lineRect.x += 14;
//                    lineRect.width -= 14;
//                    lineRect.height = 1;
//                    GUI.DrawTexture(lineRect, EditorGUIUtility.whiteTexture);
//                    GUI.color = col;
//                    var labelRect = new Rect(currentRect.x, currentRect.y, correctedLabelWidth, currentRect.height);
//                    var valueRect = new Rect(currentRect.x + correctedLabelWidth, currentRect.y, currentRect.width - correctedLabelWidth, currentRect.height);
//                    OnDrawModifier(modifier, labelRect, valueRect);
//                }
//            }
//        }

//        protected virtual void OnDrawModifier(StatModifierBase<object> modifier, Rect labelRect, Rect valueRect) {
//            EditorGUI.LabelField(labelRect, modifier.Description);
//        }

//        protected static void DrawTimerModifier<T>(Rect labelRect, Rect valueRect, T timerModifier, System.Action onDrawValue) where T : StatModifierBase<object> {
//            float duration = (float)typeof(T)
//                                .GetProperty("Duration", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
//                                .GetValue(timerModifier);
//            float remaining = (float)typeof(T)
//                                .GetProperty("Remaining", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
//                                .GetValue(timerModifier);

//            var guiColor = GUI.color;
//            var descriptionColor = GetColorFromString(timerModifier.Description);

//            var lightenColor = descriptionColor * 2f;
//            lightenColor.a = 1;
//            GUI.color = lightenColor;
//            EditorGUI.LabelField(labelRect, timerModifier.Description);

//            var dimmedColor = descriptionColor * 0.5f;
//            dimmedColor.a = 1;
//            GUI.color = dimmedColor;
//            var bgBarRect = valueRect;
//            bgBarRect.x += 2;
//            bgBarRect.width -= 3;
//            bgBarRect.y += 2;
//            bgBarRect.height -= 4;
//            GUI.DrawTexture(bgBarRect, EditorGUIUtility.whiteTexture);
//            GUI.color = descriptionColor;

//            var fgBarRect = bgBarRect;
//            fgBarRect.width *= 1 - ((duration - remaining) / duration);
//            GUI.DrawTexture(fgBarRect, EditorGUIUtility.whiteTexture);
//            GUI.color = GetContrastColor(descriptionColor);

//            onDrawValue?.Invoke();
//            GUI.color = guiColor;
//        }

//        private static List<StatModifierBase<object>> GetModifiers(StatBase<object> stat) {
//            var modifiersField = typeof(Lautaro.Stats.Engine.StatBase<object>).GetField("modifiers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
//            var modifiers = modifiersField.GetValue(stat) as List<Lautaro.Stats.Engine.StatModifierBase<object>>;
//            if (modifiers == null) {
//                modifiers = new List<Lautaro.Stats.Engine.StatModifierBase<object>>();
//                modifiersField.SetValue(stat, modifiers);
//            }
//            return modifiers;
//        }

//        public static object GetTargetObjectOfProperty(SerializedProperty prop) {
//            if (prop == null) return null;

//            var path = prop.propertyPath.Replace(".Array.data[", "[");
//            object obj = prop.serializedObject.targetObject;
//            var elements = path.Split('.');
//            foreach (var element in elements) {
//                if (element.Contains("[")) {
//                    var elementName = element.Substring(0, element.IndexOf("["));
//                    var index = System.Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
//                    obj = GetValue_Imp(obj, elementName, index);
//                }
//                else {
//                    obj = GetValue_Imp(obj, element);
//                }
//            }
//            return obj;
//        }

//        private static object GetValue_Imp(object source, string name) {
//            if (source == null)
//                return null;
//            var type = source.GetType();

//            while (type != null) {
//                var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
//                if (f != null)
//                    return f.GetValue(source);

//                var p = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
//                if (p != null)
//                    return p.GetValue(source, null);

//                type = type.BaseType;
//            }
//            return null;
//        }

//        private static object GetValue_Imp(object source, string name, int index) {
//            var enumerable = GetValue_Imp(source, name) as System.Collections.IEnumerable;
//            if (enumerable == null) return null;
//            var enm = enumerable.GetEnumerator();

//            for (int i = 0; i <= index; i++) {
//                if (!enm.MoveNext()) return null;
//            }
//            return enm.Current;
//        }

//        protected static Color GetColorFromString(string stringInput) {
//            if (stringInput == null) stringInput= string.Empty;

//            float h = (float)System.Math.Abs(stringInput.GetHashCode()) / int.MaxValue;
//            return Color.HSVToRGB(h, 0.6f, 1.0f);
//        }

//        protected static Color GetContrastColor(Color color) {
//            float h, s, l;
//            Color.RGBToHSV(color, out h, out s, out l);
//            l = 1.0f - l;
//            Color contrastColor = Color.HSVToRGB(h, s, l);
//            return contrastColor;
//        }

//    }

//}