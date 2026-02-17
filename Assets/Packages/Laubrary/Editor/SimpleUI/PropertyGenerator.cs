using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Laubrary.SimpleUI.Editor
{
    public static class PropertyGenerator
    {
        [MenuItem("Assets/SimpleUI/Generate Properties", true)]
        static bool ValidateGenerateProperties()
        {
            return Selection.activeObject is MonoScript;
        }

        [MenuItem("Assets/SimpleUI/Generate Properties")]
        static void GenerateProperties()
        {
            var script = Selection.activeObject as MonoScript;
            var classType = script.GetClass();

            if (!ValidateClass(classType))
                return;

            string scriptPath = AssetDatabase.GetAssetPath(script);
            string sourceCode = File.ReadAllText(scriptPath);

            if (!EnsureClassIsPartial(scriptPath, sourceCode, classType.Name))
                return;

            sourceCode = File.ReadAllText(scriptPath);

            var generateAttr = classType.GetCustomAttribute<GenerateSimpleUIPropertiesAttribute>();
            string generatedCode = GeneratePartialClass(classType, generateAttr.Mode);

            string directory = Path.GetDirectoryName(scriptPath);
            string fileName = Path.GetFileNameWithoutExtension(scriptPath);
            string generatedPath = Path.Combine(directory, $"{fileName}.Generated.cs");

            File.WriteAllText(generatedPath, generatedCode);
            AssetDatabase.Refresh();

            Debug.Log($"✅ Generated properties for {classType.Name} at {generatedPath}");
        }

        [MenuItem("Assets/SimpleUI/Regenerate All Properties")]
        static void RegenerateAllProperties()
        {
            var allScripts = AssetDatabase.FindAssets("t:MonoScript")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<MonoScript>(path))
                .Where(script => script != null && script.GetClass() != null)
                .ToArray();

            int count = 0;
            foreach (var script in allScripts)
            {
                var classType = script.GetClass();
                if (classType == null)
                    continue;

                if (!typeof(SimpleUIPoco).IsAssignableFrom(classType))
                    continue;

                var generateAttr = classType.GetCustomAttribute<GenerateSimpleUIPropertiesAttribute>();
                if (generateAttr == null || generateAttr.Mode == GenerateMode.None)
                    continue;

                string scriptPath = AssetDatabase.GetAssetPath(script);
                string sourceCode = File.ReadAllText(scriptPath);

                if (!IsClassPartial(sourceCode, classType.Name))
                {
                    Debug.LogWarning($"Skipping {classType.Name} - not declared as partial");
                    continue;
                }

                string generatedCode = GeneratePartialClass(classType, generateAttr.Mode);
                string directory = Path.GetDirectoryName(scriptPath);
                string fileName = Path.GetFileNameWithoutExtension(scriptPath);
                string generatedPath = Path.Combine(directory, $"{fileName}.Generated.cs");

                File.WriteAllText(generatedPath, generatedCode);
                count++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"✅ Regenerated properties for {count} classes");
        }

        static bool ValidateClass(Type classType)
        {
            if (classType == null)
            {
                ShowError("Could not load class from script");
                return false;
            }

            if (!typeof(SimpleUIPoco).IsAssignableFrom(classType))
            {
                ShowError($"Class '{classType.Name}' must inherit from SimpleUIPoco");
                return false;
            }

            var generateAttr = classType.GetCustomAttribute<GenerateSimpleUIPropertiesAttribute>();
            if (generateAttr == null)
            {
                ShowError($"Class '{classType.Name}' must have [GenerateProperties] attribute.\n\n" +
                    "Add this to your class:\n\n" +
                    "[GenerateProperties(GenerateMode.All)]\n" +
                    "public partial class " + classType.Name + " : SimpleUIPoco");
                return false;
            }

            if (generateAttr.Mode == GenerateMode.None)
            {
                ShowError($"GenerateMode is set to None for '{classType.Name}'.\n\n" +
                    "Change to GenerateMode.All or GenerateMode.OptIn");
                return false;
            }

            return true;
        }

        static bool EnsureClassIsPartial(string scriptPath, string sourceCode, string className)
        {
            if (IsClassPartial(sourceCode, className))
                return true;

            bool fix = EditorUtility.DisplayDialog(
                "Add 'partial' Keyword?",
                $"The class '{className}' needs to be 'partial' for property generation.\n\n" +
                $"Before: public class {className}\n" +
                $"After:  public partial class {className}\n\n" +
                "Add the 'partial' keyword automatically?",
                "Yes, Add It",
                "Cancel");

            if (!fix)
                return false;

            try
            {
                string updatedCode = AddPartialKeyword(sourceCode, className);
                File.WriteAllText(scriptPath, updatedCode);
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(scriptPath);

                Debug.Log($"✅ Added 'partial' keyword to {className}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to add 'partial' keyword: {e.Message}");
                return false;
            }
        }

        static bool IsClassPartial(string sourceCode, string className)
        {
            string pattern = $@"\bpartial\s+(?:sealed\s+|abstract\s+)?class\s+{Regex.Escape(className)}\b";
            return Regex.IsMatch(sourceCode, pattern);
        }

        static string AddPartialKeyword(string sourceCode, string className)
        {
            string pattern = $@"(\b(?:public|internal|private|protected)\s+(?:sealed\s+|abstract\s+)?)(class\s+{Regex.Escape(className)}\b)";
            string replacement = "$1partial $2";

            return Regex.Replace(sourceCode, pattern, replacement);
        }

        static string GeneratePartialClass(Type classType, GenerateMode mode)
        {
            var fields = classType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => f.Name.StartsWith("_"));

            var fieldsToGenerate = fields.Where(f =>
            {
                bool hasIgnore = f.GetCustomAttribute<IgnorePropertyGenerationAttribute>() != null;
                bool hasSimpleUIIgnore = f.GetCustomAttribute<SimpleUIIgnoreAttribute>() != null;
                bool hasGenerate = f.GetCustomAttribute<GeneratePropertyAttribute>() != null;

                if (mode == GenerateMode.All)
                    return !hasIgnore && !hasSimpleUIIgnore;
                else if (mode == GenerateMode.OptIn)
                    return hasGenerate && !hasSimpleUIIgnore;
                else
                    return false;
            }).ToArray();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// This code was generated by SimpleUI Property Generator");
            sb.AppendLine("// Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(classType.Namespace))
            {
                sb.AppendLine($"namespace {classType.Namespace}");
                sb.AppendLine("{");
            }

            string indent = string.IsNullOrEmpty(classType.Namespace) ? "" : "    ";

            sb.AppendLine($"{indent}public partial class {classType.Name}");
            sb.AppendLine($"{indent}{{");

            foreach (var field in fieldsToGenerate)
            {
                string propertyName = GetPropertyName(field.Name);
                string fieldType = GetFriendlyTypeName(field.FieldType);

                var simpleUIAttributes = field.GetCustomAttributes(false)
                    .Where(attr => attr.GetType().Namespace == "Laubrary.SimpleUI" && 
                           attr.GetType().Name != "GeneratePropertyAttribute" &&
                           attr.GetType().Name != "IgnorePropertyGenerationAttribute")
                    .ToArray();

                foreach (var attr in simpleUIAttributes)
                {
                    sb.AppendLine($"{indent}    {FormatAttribute(attr)}");
                }

                sb.AppendLine($"{indent}    public {fieldType} {propertyName}");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        get => {field.Name};");
                sb.AppendLine($"{indent}        set => SetField(ref {field.Name}, value);");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(classType.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        static string GetPropertyName(string fieldName)
        {
            string name = fieldName.TrimStart('_');
            if (string.IsNullOrEmpty(name))
                return fieldName;

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        static string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(double)) return "double";
            if (type == typeof(long)) return "long";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(decimal)) return "decimal";

            if (type.IsGenericType)
            {
                string genericName = type.Name.Substring(0, type.Name.IndexOf('`'));
                var genericArgs = type.GetGenericArguments();
                string genericArgNames = string.Join(", ", genericArgs.Select(GetFriendlyTypeName));
                return $"{genericName}<{genericArgNames}>";
            }

            return type.Name;
        }

        static string FormatAttribute(object attribute)
        {
            var attrType = attribute.GetType();
            string attrName = attrType.Name.Replace("Attribute", "");

            if (attrType.Name == "SimpleUIPathAttribute")
            {
                var pathProp = attrType.GetProperty("Path");
                if (pathProp != null)
                {
                    string path = pathProp.GetValue(attribute) as string;
                    return $"[{attrName}(\"{path}\")]";
                }
            }

            if (attrType.Name == "SimpleUIFormatAttribute")
            {
                var formatProp = attrType.GetProperty("Format");
                if (formatProp != null)
                {
                    string format = formatProp.GetValue(attribute) as string;
                    return $"[{attrName}(\"{format}\")]";
                }
            }

            if (attrType.Name == "SimpleUIComponentAttribute")
            {
                var componentProp = attrType.GetProperty("ComponentType");
                if (componentProp != null)
                {
                    Type componentType = componentProp.GetValue(attribute) as Type;
                    return $"[{attrName}(typeof({componentType.Name}))]";
                }
            }

            return $"[{attrName}]";
        }

        static void ShowError(string message)
        {
            EditorUtility.DisplayDialog("SimpleUI Property Generator", message, "OK");
        }
    }
}
