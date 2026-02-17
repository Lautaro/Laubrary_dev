using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Laubrary.SimpleUI
{
    internal class MemberAccessor
    {
        private readonly FieldInfo _field;
        private readonly PropertyInfo _property;

        public string Name => _field != null ? _field.Name : _property.Name;
        public Type MemberType => _field != null ? _field.FieldType : _property.PropertyType;
        public bool IsField => _field != null;
        public bool IsProperty => _property != null;

        public MemberAccessor(FieldInfo field)
        {
            _field = field;
        }

        public MemberAccessor(PropertyInfo property)
        {
            _property = property;
        }

        public object GetValue(object obj)
        {
            return _field != null ? _field.GetValue(obj) : _property.GetValue(obj);
        }

        public void SetValue(object obj, object value)
        {
            if (_field != null)
                _field.SetValue(obj, value);
            else if (_property.CanWrite)
                _property.SetValue(obj, value);
        }

        public T GetCustomAttribute<T>() where T : Attribute
        {
            return _field != null 
                ? _field.GetCustomAttribute<T>() 
                : _property.GetCustomAttribute<T>();
        }

        public T[] GetCustomAttributes<T>() where T : Attribute
        {
            return _field != null 
                ? _field.GetCustomAttributes<T>().ToArray() 
                : _property.GetCustomAttributes<T>().ToArray();
        }
    }

    public class BindingResolver
    {
        private readonly Transform _root;
        private readonly SimpleUIAttribute _config;

        public BindingResolver(Transform root, SimpleUIAttribute config)
        {
            _root = root;
            _config = config;
        }

        public BindingCache<T> Resolve<T>() where T : class
        {
            var reports = new List<BindingReport>();
            var members = new List<MemberAccessor>();

            // Get all properties first
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var property in properties)
            {
                if (property.CanRead)
                {
                    members.Add(new MemberAccessor(property));
                    propertyNames.Add(property.Name);
                }
            }

            // Get fields, but skip private backing fields that have a corresponding property
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                // Skip compiler-generated backing fields (e.g., <PropertyName>k__BackingField)
                if (field.Name.Contains("<") || field.Name.Contains(">"))
                    continue;

                // Skip private fields that start with _ if a matching property exists
                // e.g., skip "_health" if "health" property exists
                if (!field.IsPublic && field.Name.StartsWith("_"))
                {
                    string potentialPropertyName = field.Name.Substring(1);  // Remove leading underscore
                    if (propertyNames.Contains(potentialPropertyName))
                        continue;  // Skip this field - the property will handle it
                }

                members.Add(new MemberAccessor(field));
            }

            var tempCache = new BindingCache<T>(_config, new BindingReport[0]);

            foreach (var member in members)
            {
                var report = ResolveMember<T>(member, tempCache);
                reports.Add(report);
            }

            var cache = new BindingCache<T>(_config, reports.ToArray());
            cache.CopyBindingsFrom(tempCache);
            return cache;
        }

        private BindingReport ResolveMemberToPath<T>(MemberAccessor member, string path, BindingCache<T> cache, bool isExplicitPath) where T : class
        {
            var report = new BindingReport
            {
                FieldName = member.Name,
                FieldType = member.MemberType,
                DecisionTrace = new List<string>().ToArray()
            };

            var trace = new List<string>();
            trace.Add($"Resolving member to path: '{path}'");

            var targetObject = FindGameObjectByPath(path);
            if (targetObject == null)
            {
                report.Status = BindingStatus.Error;
                report.ErrorMessage = $"GameObject at path '{path}' not found";
                report.SuggestedFixes = new[] { $"Create GameObject at path '{path}' or verify path is correct" };
                report.DecisionTrace = trace.ToArray();
                return report;
            }

            trace.Add($"Found GameObject at '{path}'");
            report.GameObjectPath = GetGameObjectPath(targetObject);
            report.ResolverStep = "AttributePath";
            report.Confidence = ConfidenceLevel.Explicit;

            var componentAttr = member.GetCustomAttribute<SimpleUIComponentAttribute>();
            Component targetComponent = null;

            if (componentAttr != null)
            {
                trace.Add($"Attempting to get component: {componentAttr.ComponentType.Name}");
                targetComponent = targetObject.GetComponent(componentAttr.ComponentType);

                if (targetComponent == null)
                {
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"Component {componentAttr.ComponentType.Name} not found on '{report.GameObjectPath}'";
                    report.SuggestedFixes = new[] { $"Add {componentAttr.ComponentType.Name} component to '{report.GameObjectPath}'" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                trace.Add($"Found component: {componentAttr.ComponentType.Name}");
                report.ComponentType = componentAttr.ComponentType;
            }
            else
            {
                trace.Add($"No [SimpleUIComponent] - attempting type inference for {member.MemberType.Name}");
                var compatibleComponents = GetCompatibleComponents(targetObject, member.MemberType);

                if (compatibleComponents.Count == 0)
                {
                    var expectedTypes = GetExpectedComponentTypes(member.MemberType);
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"No compatible component on '{report.GameObjectPath}' for member type {member.MemberType.Name}";
                    report.SuggestedFixes = new[] { $"Add one of these components: {string.Join(", ", expectedTypes.Select(t => t.Name))}" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                if (compatibleComponents.Count > 1)
                {
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"Ambiguous: GameObject '{report.GameObjectPath}' has {compatibleComponents.Count} compatible components: {string.Join(", ", compatibleComponents.Select(c => c.GetType().Name))}";
                    report.SuggestedFixes = new[] { $"Add [SimpleUIComponent(typeof(...))] to specify which component to use" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                targetComponent = compatibleComponents[0];
                trace.Add($"Found single compatible component: {targetComponent.GetType().Name}");
                report.ComponentType = targetComponent.GetType();
            }

            var formatAttr = member.GetCustomAttribute<SimpleUIFormatAttribute>();
            CreateBinding(member, targetComponent, formatAttr?.Format, cache, trace, report);

            report.Status = BindingStatus.Success;
            report.DecisionTrace = trace.ToArray();
            return report;
        }

        private BindingReport ResolveMember<T>(MemberAccessor member, BindingCache<T> cache) where T : class
        {
            var report = new BindingReport
            {
                FieldName = member.Name,
                FieldType = member.MemberType,
                DecisionTrace = new List<string>().ToArray()
            };

            var trace = new List<string>();

            if (member.GetCustomAttribute<SimpleUIIgnoreAttribute>() != null)
            {
                trace.Add("Member has [SimpleUIIgnore] - skipping");
                report.Status = BindingStatus.Skipped;
                report.DecisionTrace = trace.ToArray();
                return report;
            }

            var pathAttrs = member.GetCustomAttributes<SimpleUIPathAttribute>();
            
            bool shouldBind = _config.bindAll;
            if (!_config.bindAll)
            {
                // Opt-in binding: field/property must have at least one of these attributes
                shouldBind = pathAttrs.Length > 0 ||
                           member.GetCustomAttribute<SimpleUIComponentAttribute>() != null ||
                           member.GetCustomAttribute<SimpleUIFormatAttribute>() != null ||
                           member.GetCustomAttribute<SimpleUIBindAttribute>() != null;
            }

            if (!shouldBind)
            {
                trace.Add($"bindAll=false and no binding attributes - skipping");
                report.Status = BindingStatus.Skipped;
                report.DecisionTrace = trace.ToArray();
                return report;
            }

            if (pathAttrs.Length > 1)
            {
                trace.Add($"Multiple [SimpleUIPath] attributes found - creating {pathAttrs.Length} bindings");
                
                var subReports = new List<BindingReport>();
                foreach (var pathAttr in pathAttrs)
                {
                    var subReport = ResolveMemberToPath<T>(member, pathAttr.Path, cache, true);
                    subReports.Add(subReport);
                }
                
                var successCount = subReports.Count(r => r.Status == BindingStatus.Success);
                report.Status = successCount > 0 ? BindingStatus.Success : BindingStatus.Error;
                report.ErrorMessage = successCount > 0 
                    ? $"Bound to {successCount}/{pathAttrs.Length} paths" 
                    : "All path bindings failed";
                report.DecisionTrace = trace.Concat(subReports.SelectMany(r => r.DecisionTrace)).ToArray();
                report.GameObjectPath = subReports.FirstOrDefault(r => r.Status == BindingStatus.Success)?.GameObjectPath;
                report.ComponentType = subReports.FirstOrDefault(r => r.Status == BindingStatus.Success)?.ComponentType;
                report.Confidence = ConfidenceLevel.Explicit;
                
                if (successCount < pathAttrs.Length)
                {
                    var failedPaths = subReports.Where(r => r.Status == BindingStatus.Error)
                        .Select(r => r.ErrorMessage).ToArray();
                    report.SuggestedFixes = failedPaths;
                }
                
                return report;
            }

            GameObject targetObject = null;
            
            if (pathAttrs.Length == 1)
            {
                trace.Add($"Attempting attribute path: '{pathAttrs[0].Path}'");
                targetObject = FindGameObjectByPath(pathAttrs[0].Path);

                if (targetObject == null)
                {
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"GameObject at path '{pathAttrs[0].Path}' not found";
                    report.SuggestedFixes = new[] { $"Create GameObject at path '{pathAttrs[0].Path}' or verify path is correct" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                trace.Add($"Found GameObject at '{pathAttrs[0].Path}'");
                report.ResolverStep = "AttributePath";
                report.Confidence = ConfidenceLevel.Explicit;
            }
            else
            {
                trace.Add("No [SimpleUIPath] - attempting name-based search");
                targetObject = FindGameObjectByName(member.Name, trace, out var confidence, out var errorMsg, out var suggestions);

                if (targetObject == null)
                {
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = errorMsg;
                    report.SuggestedFixes = suggestions;
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                report.Confidence = confidence;
            }

            report.GameObjectPath = GetGameObjectPath(targetObject);

            var componentAttr = member.GetCustomAttribute<SimpleUIComponentAttribute>();
            Component targetComponent = null;

            if (componentAttr != null)
            {
                trace.Add($"Attempting to get component: {componentAttr.ComponentType.Name}");
                targetComponent = targetObject.GetComponent(componentAttr.ComponentType);

                if (targetComponent == null)
                {
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"Component {componentAttr.ComponentType.Name} not found on '{report.GameObjectPath}'";
                    report.SuggestedFixes = new[] { $"Add {componentAttr.ComponentType.Name} component to '{report.GameObjectPath}'" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                trace.Add($"Found component: {componentAttr.ComponentType.Name}");
                report.ComponentType = componentAttr.ComponentType;
                report.Confidence = ConfidenceLevel.Explicit;
            }
            else
            {
                trace.Add($"No [SimpleUIComponent] - attempting type inference for {member.MemberType.Name}");
                var compatibleComponents = GetCompatibleComponents(targetObject, member.MemberType);

                if (compatibleComponents.Count == 0)
                {
                    var expectedTypes = GetExpectedComponentTypes(member.MemberType);
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"No compatible component on '{report.GameObjectPath}' for member type {member.MemberType.Name}";
                    report.SuggestedFixes = new[] { $"Add one of these components: {string.Join(", ", expectedTypes.Select(t => t.Name))}" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                if (compatibleComponents.Count > 1)
                {
                    report.Status = BindingStatus.Error;
                    report.ErrorMessage = $"Ambiguous: GameObject '{report.GameObjectPath}' has {compatibleComponents.Count} compatible components: {string.Join(", ", compatibleComponents.Select(c => c.GetType().Name))}";
                    report.SuggestedFixes = new[] { $"Add [SimpleUIComponent(typeof(...))] to specify which component to use" };
                    report.DecisionTrace = trace.ToArray();
                    return report;
                }

                targetComponent = compatibleComponents[0];
                trace.Add($"Found single compatible component: {targetComponent.GetType().Name}");
                report.ComponentType = targetComponent.GetType();
                if (report.Confidence != ConfidenceLevel.Explicit)
                {
                    report.Confidence = ConfidenceLevel.Weak;
                }
            }

            var formatAttr = member.GetCustomAttribute<SimpleUIFormatAttribute>();
            CreateBinding(member, targetComponent, formatAttr?.Format, cache, trace, report);

            report.Status = BindingStatus.Success;
            report.DecisionTrace = trace.ToArray();
            return report;
        }

        private GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            Transform current = _root;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                Transform found = null;
                foreach (Transform child in current)
                {
                    if (child.name == part)
                    {
                        found = child;
                        break;
                    }
                }

                if (found == null)
                    return null;

                current = found;
            }

            return current.gameObject;
        }

        private GameObject FindGameObjectByName(string fieldName, List<string> trace, out ConfidenceLevel confidence, out string errorMsg, out string[] suggestions)
        {
            confidence = ConfidenceLevel.Strong;
            errorMsg = null;
            suggestions = null;

            var searchName = GetSearchName(fieldName);
            trace.Add($"Searching for GameObject matching '{searchName}'");

            var directChild = FindInDirectChildren(searchName);
            if (directChild != null)
            {
                trace.Add($"Found in direct children: {directChild.name}");
                return directChild;
            }

            trace.Add("Not in direct children - performing BFS search");
            var matches = FindInSubtree(searchName, _config.maxSearchDepth);

            if (matches.Count == 0)
            {
                errorMsg = $"No GameObject named '{searchName}' found";
                suggestions = new[] { $"Add [SimpleUIPath(\"path/to/object\")] or create child GameObject named '{searchName}'" };
                return null;
            }

            if (matches.Count > 1)
            {
                if (_config.multipleMatchBehavior == MultiMatchMode.Fail)
                {
                    var paths = string.Join(", ", matches.Select(go => GetGameObjectPath(go)));
                    errorMsg = $"Ambiguous: {matches.Count} GameObjects named '{searchName}' found at: {paths}";
                    suggestions = new[] { "Add [SimpleUIPath(\"exact/path\")] to disambiguate" };
                    return null;
                }
                else
                {
                    trace.Add($"Multiple matches found ({matches.Count}), using first (multipleMatchBehavior=UseFirst)");
                    Debug.LogWarning($"[SimpleUI] Multiple matches for '{searchName}', using first: {GetGameObjectPath(matches[0])}");
                    return matches[0];
                }
            }

            trace.Add($"Found single match: {GetGameObjectPath(matches[0])}");
            return matches[0];
        }

        private string GetSearchName(string fieldName)
        {
            switch (_config.nameMatching)
            {
                case NameMatchMode.Exact:
                    return fieldName;
                case NameMatchMode.CaseInsensitive:
                    return fieldName.ToLowerInvariant();
                case NameMatchMode.Loose:
                    return fieldName.ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", "");
                default:
                    return fieldName;
            }
        }

        private bool MatchesName(string objectName, string searchName)
        {
            switch (_config.nameMatching)
            {
                case NameMatchMode.Exact:
                    return objectName == searchName;
                case NameMatchMode.CaseInsensitive:
                    return objectName.ToLowerInvariant() == searchName;
                case NameMatchMode.Loose:
                    var normalized = objectName.ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", "");
                    return normalized == searchName;
                default:
                    return objectName == searchName;
            }
        }

        private GameObject FindInDirectChildren(string searchName)
        {
            foreach (Transform child in _root)
            {
                if (MatchesName(child.name, searchName))
                    return child.gameObject;
            }
            return null;
        }

        private List<GameObject> FindInSubtree(string searchName, int maxDepth)
        {
            var matches = new List<GameObject>();
            BFSSearch(_root, searchName, maxDepth, 0, matches);
            return matches;
        }

        private void BFSSearch(Transform current, string searchName, int maxDepth, int currentDepth, List<GameObject> matches)
        {
            if (currentDepth > maxDepth)
                return;

            foreach (Transform child in current)
            {
                if (MatchesName(child.name, searchName))
                {
                    matches.Add(child.gameObject);
                }

                BFSSearch(child, searchName, maxDepth, currentDepth + 1, matches);
            }
        }

        private List<Component> GetCompatibleComponents(GameObject go, Type fieldType)
        {
            var compatible = new List<Component>();

            if (fieldType == typeof(string))
            {
                AddIfExists<TMP_Text>(go, compatible);
                AddIfExists<TMP_InputField>(go, compatible);
            }
            else if (fieldType == typeof(int) || fieldType == typeof(float) || fieldType == typeof(double))
            {
                AddIfExists<TMP_Text>(go, compatible);
                AddIfExists<Slider>(go, compatible);
            }
            else if (fieldType == typeof(bool))
            {
                AddIfExists<TMP_Text>(go, compatible);
                AddIfExists<Toggle>(go, compatible);
            }
            else if (fieldType == typeof(Sprite))
            {
                AddIfExists<Image>(go, compatible);
            }
            else if (fieldType == typeof(Color))
            {
                AddIfExists<Image>(go, compatible);
                AddIfExists<TMP_Text>(go, compatible);
            }
            else if (fieldType.IsEnum)
            {
                AddIfExists<TMP_Dropdown>(go, compatible);
            }
            else if (fieldType == typeof(System.TimeSpan) || fieldType == typeof(System.DateTime))
            {
                // TimeSpan and DateTime can be formatted as text
                AddIfExists<TMP_Text>(go, compatible);
            }

            return compatible;
        }

        private void AddIfExists<T>(GameObject go, List<Component> list) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp != null)
                list.Add(comp);
        }

        private Type[] GetExpectedComponentTypes(Type fieldType)
        {
            if (fieldType == typeof(string))
                return new[] { typeof(TMP_Text), typeof(TMP_InputField) };
            else if (fieldType == typeof(int) || fieldType == typeof(float) || fieldType == typeof(double))
                return new[] { typeof(TMP_Text), typeof(Slider) };
            else if (fieldType == typeof(bool))
                return new[] { typeof(TMP_Text), typeof(Toggle) };
            else if (fieldType == typeof(Sprite))
                return new[] { typeof(Image) };
            else if (fieldType == typeof(Color))
                return new[] { typeof(Image), typeof(TMP_Text) };
            else if (fieldType.IsEnum)
                return new[] { typeof(TMP_Dropdown) };
            else if (fieldType == typeof(System.TimeSpan) || fieldType == typeof(System.DateTime))
                return new[] { typeof(TMP_Text) };

            return new Type[0];
        }

        private void CreateBinding<T>(MemberAccessor member, Component component, string format, BindingCache<T> cache, List<string> trace, BindingReport report) where T : class
        {
            var bindingEntry = new BindingCache<T>.BindingEntry { FieldName = member.Name };
            bool isTwoWay = false;

            if (component is TMP_Text tmpText)
            {
                bindingEntry.UpdateUI = poco =>
                {
                    var value = member.GetValue(poco);
                    tmpText.text = FormatValue(value, format);
                };
                trace.Add("Created one-way binding to TMP_Text.text");
            }
            else if (component is TMP_InputField tmpInput)
            {
                bindingEntry.UpdateUI = poco =>
                {
                    var value = member.GetValue(poco);
                    tmpInput.text = FormatValue(value, format);
                };

                bindingEntry.SetupTwoWay = poco =>
                {
                    tmpInput.onValueChanged.AddListener(newValue =>
                    {
                        member.SetValue(poco, newValue);
                    });
                };

                cache.AddCleanupAction(() => tmpInput.onValueChanged.RemoveAllListeners());
                isTwoWay = true;
                trace.Add("Created two-way binding to TMP_InputField");
            }
            else if (component is Slider slider)
            {
                bindingEntry.UpdateUI = poco =>
                {
                    var value = member.GetValue(poco);
                    slider.value = Convert.ToSingle(value);
                };

                bindingEntry.SetupTwoWay = poco =>
                {
                    slider.onValueChanged.AddListener(newValue =>
                    {
                        if (member.MemberType == typeof(int))
                            member.SetValue(poco, Mathf.RoundToInt(newValue));
                        else
                            member.SetValue(poco, newValue);
                    });
                };

                cache.AddCleanupAction(() => slider.onValueChanged.RemoveAllListeners());
                isTwoWay = true;
                trace.Add("Created two-way binding to Slider");
            }
            else if (component is Toggle toggle)
            {
                bindingEntry.UpdateUI = poco =>
                {
                    var value = member.GetValue(poco);
                    toggle.isOn = (bool)value;
                };

                bindingEntry.SetupTwoWay = poco =>
                {
                    toggle.onValueChanged.AddListener(newValue =>
                    {
                        member.SetValue(poco, newValue);
                    });
                };

                cache.AddCleanupAction(() => toggle.onValueChanged.RemoveAllListeners());
                isTwoWay = true;
                trace.Add("Created two-way binding to Toggle");
            }
            else if (component is Image image)
            {
                if (member.MemberType == typeof(Sprite))
                {
                    bindingEntry.UpdateUI = poco =>
                    {
                        var value = member.GetValue(poco);
                        image.sprite = (Sprite)value;
                    };
                    trace.Add("Created one-way binding to Image.sprite");
                }
                else if (member.MemberType == typeof(Color))
                {
                    bindingEntry.UpdateUI = poco =>
                    {
                        var value = member.GetValue(poco);
                        image.color = (Color)value;
                    };
                    trace.Add("Created one-way binding to Image.color");
                }
            }
            else if (component is TMP_Dropdown dropdown)
            {
                if (member.MemberType.IsEnum)
                {
                    var enumNames = Enum.GetNames(member.MemberType);
                    dropdown.ClearOptions();
                    dropdown.AddOptions(enumNames.ToList());

                    bindingEntry.UpdateUI = poco =>
                    {
                        var value = member.GetValue(poco);
                        dropdown.value = Convert.ToInt32(value);
                    };

                    bindingEntry.SetupTwoWay = poco =>
                    {
                        dropdown.onValueChanged.AddListener(newValue =>
                        {
                            var enumValue = Enum.ToObject(member.MemberType, newValue);
                            member.SetValue(poco, enumValue);
                        });
                    };

                    cache.AddCleanupAction(() => dropdown.onValueChanged.RemoveAllListeners());
                    isTwoWay = true;
                    trace.Add("Created two-way binding to TMP_Dropdown with auto-populated enum options");
                }
            }

            cache.AddBinding(bindingEntry);
            report.Mode = isTwoWay ? BindingMode.TwoWay : BindingMode.OneWay;
        }

        private string FormatValue(object value, string format)
        {
            if (value == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(format))
                return string.Format(format, value);

            return value.ToString();
        }

        private string GetGameObjectPath(GameObject go)
        {
            if (go.transform == _root)
                return go.name;

            var path = go.name;
            var current = go.transform.parent;

            while (current != null && current != _root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
