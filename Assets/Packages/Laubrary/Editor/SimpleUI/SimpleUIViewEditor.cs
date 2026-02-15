using UnityEditor;
using UnityEngine;
using Laubrary.SimpleUI;
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;

namespace Laubrary.SimpleUI.Editor
{
    [CustomEditor(typeof(SimpleUIView))]
    public class SimpleUIViewEditor : UnityEditor.Editor
    {
        private SerializedProperty _pocoTypeNameProp;
        private string[] _simpleUITypeNames;
        private Type[] _simpleUITypes;
        private int _selectedIndex = -1;
        private bool _showPocoData = true;
        private bool _showBindings = false;

        void OnEnable()
        {
            _pocoTypeNameProp = serializedObject.FindProperty("pocoTypeName");

            _simpleUITypes = TypeCache.GetTypesWithAttribute<SimpleUIAttribute>()
                .Where(t => t.IsClass && !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToArray();

            _simpleUITypeNames = _simpleUITypes.Select(t => t.Name).ToArray();

            var currentTypeName = _pocoTypeNameProp.stringValue;
            if (!string.IsNullOrEmpty(currentTypeName))
            {
                _selectedIndex = Array.FindIndex(_simpleUITypes, 
                    t => t.AssemblyQualifiedName == currentTypeName);
            }

            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (Application.isPlaying && target != null)
            {
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var view = (SimpleUIView)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SimpleUI View", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (_simpleUITypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No types with [SimpleUI] attribute found. Create a class with [SimpleUI] attribute to use SimpleUIView.",
                    MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var newIndex = EditorGUILayout.Popup("POCO Type", _selectedIndex, _simpleUITypeNames);
            
            if (EditorGUI.EndChangeCheck() && newIndex >= 0)
            {
                _selectedIndex = newIndex;
                _pocoTypeNameProp.stringValue = _simpleUITypes[newIndex].AssemblyQualifiedName;
                serializedObject.ApplyModifiedProperties();
                
                if (Application.isPlaying)
                {
                    EditorUtility.DisplayDialog("POCO Type Changed", 
                        "Exit and re-enter Play Mode for changes to take effect.", "OK");
                }
            }

            if (_selectedIndex >= 0)
            {
                var selectedType = _simpleUITypes[_selectedIndex];
                var attr = (SimpleUIAttribute)Attribute.GetCustomAttribute(
                    selectedType, typeof(SimpleUIAttribute));
                
                if (attr != null && attr.AutoRefresh)
                {
                    EditorGUILayout.HelpBox("✓ Auto-Refresh Enabled - UI updates every frame", MessageType.Info);
                }

                if (selectedType.IsSubclassOf(typeof(SimpleUIPoco)))
                {
                    EditorGUILayout.HelpBox("✓ Inherits SimpleUIPoco - Can call Refresh() for manual updates", MessageType.Info);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Binding Diagnostics", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to see binding diagnostics and POCO data.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawPocoDataInspector(view);
            
            EditorGUILayout.Space();
            _showBindings = EditorGUILayout.Foldout(_showBindings, "Binding Details", true, EditorStyles.foldoutHeader);

            if (!_showBindings)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var reports = view.GetReports();
            var pocoType = view.GetPocoType();

            if (reports == null || reports.Length == 0)
            {
                EditorGUILayout.HelpBox("No bindings found.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.LabelField($"POCO Type: {pocoType?.Name ?? "Unknown"}", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            int successCount = 0;
            int errorCount = 0;

            foreach (var report in reports)
            {
                if (report.Status == BindingStatus.Success)
                    successCount++;
                else
                    errorCount++;

                DrawBindingReport(report);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Summary: {successCount} Success, {errorCount} Errors", EditorStyles.boldLabel);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPocoDataInspector(SimpleUIView view)
        {
            var boundData = view.GetBoundData();
            if (boundData == null)
            {
                EditorGUILayout.HelpBox("No POCO data bound. Call UpdateUI(data) to bind a POCO instance.", MessageType.Info);
                return;
            }

            var pocoType = view.GetPocoType();
            
            EditorGUILayout.Space();
            _showPocoData = EditorGUILayout.Foldout(_showPocoData, $"POCO Data ({pocoType.Name})", true, EditorStyles.foldoutHeader);

            if (!_showPocoData)
            {
                return;
            }

            EditorGUI.indentLevel++;
            
            var members = GetEditableMembers(pocoType);
            bool valueChanged = false;

            foreach (var member in members)
            {
                valueChanged |= DrawMemberField(member, boundData);
            }

            EditorGUI.indentLevel--;

            if (valueChanged)
            {
                view.UpdateUI();
            }
        }

        private List<MemberData> GetEditableMembers(Type type)
        {
            var members = new List<MemberData>();

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.GetCustomAttribute<SimpleUIIgnoreAttribute>() != null)
                    continue;

                members.Add(new MemberData
                {
                    Name = field.Name,
                    MemberType = field.FieldType,
                    GetValue = obj => field.GetValue(obj),
                    SetValue = (obj, value) => field.SetValue(obj, value),
                    IsProperty = false
                });
            }

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                if (property.GetCustomAttribute<SimpleUIIgnoreAttribute>() != null)
                    continue;

                members.Add(new MemberData
                {
                    Name = property.Name,
                    MemberType = property.PropertyType,
                    GetValue = obj => property.GetValue(obj),
                    SetValue = (obj, value) => property.SetValue(obj, value),
                    IsProperty = true
                });
            }

            return members;
        }

        private bool DrawMemberField(MemberData member, object target)
        {
            var currentValue = member.GetValue(target);
            object newValue = null;
            bool changed = false;

            EditorGUI.BeginChangeCheck();

            var label = new GUIContent(member.Name, member.IsProperty ? "Property" : "Field");

            if (member.MemberType == typeof(int))
            {
                newValue = EditorGUILayout.IntField(label, (int)currentValue);
            }
            else if (member.MemberType == typeof(float))
            {
                newValue = EditorGUILayout.FloatField(label, (float)currentValue);
            }
            else if (member.MemberType == typeof(string))
            {
                newValue = EditorGUILayout.TextField(label, (string)currentValue ?? "");
            }
            else if (member.MemberType == typeof(bool))
            {
                newValue = EditorGUILayout.Toggle(label, (bool)currentValue);
            }
            else if (member.MemberType.IsEnum)
            {
                newValue = EditorGUILayout.EnumPopup(label, (Enum)currentValue);
            }
            else if (member.MemberType == typeof(double))
            {
                newValue = EditorGUILayout.DoubleField(label, (double)currentValue);
            }
            else if (member.MemberType == typeof(long))
            {
                newValue = EditorGUILayout.LongField(label, (long)currentValue);
            }
            else if (member.MemberType == typeof(Vector2))
            {
                newValue = EditorGUILayout.Vector2Field(label, (Vector2)currentValue);
            }
            else if (member.MemberType == typeof(Vector3))
            {
                newValue = EditorGUILayout.Vector3Field(label, (Vector3)currentValue);
            }
            else if (member.MemberType == typeof(Vector4))
            {
                newValue = EditorGUILayout.Vector4Field(label, (Vector4)currentValue);
            }
            else if (member.MemberType == typeof(Color))
            {
                newValue = EditorGUILayout.ColorField(label, (Color)currentValue);
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(member.MemberType))
            {
                newValue = EditorGUILayout.ObjectField(label, (UnityEngine.Object)currentValue, member.MemberType, true);
            }
            else
            {
                EditorGUILayout.LabelField(label, new GUIContent($"({member.MemberType.Name}) - not editable"));
            }

            if (EditorGUI.EndChangeCheck() && newValue != null)
            {
                member.SetValue(target, newValue);
                changed = true;
            }

            return changed;
        }

        private class MemberData
        {
            public string Name;
            public Type MemberType;
            public Func<object, object> GetValue;
            public Action<object, object> SetValue;
            public bool IsProperty;
        }

        private void DrawBindingReport(BindingReport report)
        {
            MessageType messageType = report.Status == BindingStatus.Success
                ? MessageType.Info
                : MessageType.Error;

            string icon = report.Status == BindingStatus.Success ? "✓" : "✗";
            string message = $"{icon} {report.FieldName} ({report.FieldType?.Name ?? "Unknown"})";

            if (report.Status == BindingStatus.Success)
            {
                message += $"\n   → {report.GameObjectPath} ({report.ComponentType?.Name ?? "Unknown"})";
                message += $"\n   Mode: {report.Mode} | Confidence: {report.Confidence}";
            }
            else
            {
                message += $"\n   Error: {report.ErrorMessage}";
                if (report.SuggestedFixes != null && report.SuggestedFixes.Length > 0)
                {
                    message += $"\n   Fix: {string.Join(", ", report.SuggestedFixes)}";
                }
            }

            EditorGUILayout.HelpBox(message, messageType);
        }
    }
}
