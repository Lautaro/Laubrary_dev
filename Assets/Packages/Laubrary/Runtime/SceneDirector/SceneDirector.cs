using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace Laubrary.SceneDirector
{
    [DefaultExecutionOrder(-999)]
    abstract public class SceneDirector : MonoBehaviour
    {
        // Store category to list of MonoBehaviours
        private readonly Dictionary<string, List<MonoBehaviour>> _categoryMap = new();

        void Awake()
        {
            //var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            _categoryMap.Clear();

            var fields = GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.IsDefined(typeof(DirectableAttribute), true)
                         && typeof(MonoBehaviour).IsAssignableFrom(f.FieldType));

            foreach (var f in fields)
            {
                var attr = (DirectableAttribute)f.GetCustomAttributes(typeof(DirectableAttribute), true).FirstOrDefault();
                if (attr == null) continue;

                var mb = f.GetValue(this) as MonoBehaviour;
                if (mb == null) continue;

                if (!string.IsNullOrEmpty(attr.Category))
                {
                    // Always start disabled if in a category
                    mb.gameObject.SetActive(false);

                    if (!_categoryMap.TryGetValue(attr.Category, out var list))
                    {
                        list = new List<MonoBehaviour>();
                        _categoryMap[attr.Category] = list;
                    }
                    list.Add(mb);
                }
                else
                {
                    switch (attr.StartState)
                    {
                        case StartState.Enabled:
                            mb.gameObject.SetActive(true);
                            break;
                        case StartState.Disabled:
                            mb.gameObject.SetActive(false);
                            break;
                            // Auto: do nothing
                    }
                }
            }

           // stopwatch.Stop();
          //  Debug.Log($"[SceneDirector] Awake completed in {stopwatch.Elapsed.TotalMilliseconds:F2} ms ({stopwatch.Elapsed.TotalSeconds:F4} s).", this);
        }

        [ContextMenu("Auto Assign")]
        public void AutoAssign()
        {
            // 1) Collect all fields marked [Directable] that derive from MonoBehaviour
            var fields = GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.IsDefined(typeof(DirectableAttribute), true)
                         && typeof(MonoBehaviour).IsAssignableFrom(f.FieldType))
                .ToList();

            // 2) One pass: find every MonoBehaviour (including inactive)
            var all = FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mb in all)
            {
                for (int i = fields.Count - 1; i >= 0; i--)
                {
                    var f = fields[i];
                    if (f.FieldType.IsAssignableFrom(mb.GetType()) &&
                        (mb.name == f.Name || mb.gameObject.name == f.Name))
                    {
                        f.SetValue(this, mb);
                        fields.RemoveAt(i);
                    }
                }
                if (fields.Count == 0) break;
            }

            // 3) For any remaining fields, check the two conditions before assigning
            foreach (var f in fields.ToList())
            {
                // Condition 1: Only one field of this type among Directables
                int fieldTypeCount = fields.Count(x => x.FieldType == f.FieldType);
                if (fieldTypeCount == 1)
                {
                    // Condition 2: Only one instance of this type in the scene
                    var matches = all.Where(mb => f.FieldType.IsAssignableFrom(mb.GetType())).ToList();
                    if (matches.Count == 1)
                    {
                        f.SetValue(this, matches[0]);
                        fields.Remove(f);
                    }
                }
            }

            // Log a warning for any fields that were not assigned
            // Log a warning for any fields that were not assigned
            foreach (var f in fields)
            {
                int fieldTypeCount = fields.Count(x => x.FieldType == f.FieldType);
                int sceneTypeCount = all.Count(mb => f.FieldType.IsAssignableFrom(mb.GetType()));

                Debug.LogWarning(
                    $"[SceneDirector] AutoAssign: No matching instance found for field '{f.Name}' of type '{f.FieldType.Name}'. Assignment skipped. " +
                    $"Note: If there is more than one field of this type marked [Directable] or more than one instance of this type in the scene, " +
                    $"the GameObject's name must match the field name for auto-assignment. Otherwise, no match will be made."
                );
            }
        }

        /// <summary>
        /// Enables all scripts in the given category.
        /// </summary>
        public void TriggerCategory(string category)
        {
            if (_categoryMap.TryGetValue(category, out var list))
            {
                foreach (var mb in list)
                {
                    if (mb != null)
                        mb.gameObject.SetActive(true);
                }
            }
        }
    }
    [AttributeUsage(AttributeTargets.Field)]
    public class DirectableAttribute : Attribute
    {
        public StartState StartState { get; }
        public string Category { get; }

        // Use for StartState only (no category)
        public DirectableAttribute(StartState startState)
        {
            StartState = startState;
            Category = null;
        }

        // Use for Category only (no startState)
        public DirectableAttribute(string category)
        {
            Category = category;
            StartState = StartState.Auto;
        }

        public DirectableAttribute()
        {
            Category = null;
            StartState = StartState.Auto;
        }
    }

    public enum StartState
    {
        Auto,
        Enabled,
        Disabled
    }
}