using System;
using System.Reflection;
using UnityEngine;

namespace Laubrary.SimpleUI
{
    public class SimpleUIView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The full type name of the POCO class (e.g., 'Laubrary.SimpleUI.Demo.CharacterSheet')")]
        private string pocoTypeName;

        private Type _pocoType;
        private object _cache;
        private BindingReport[] _reports;
        private bool _twoWayBindingsSetup = false;
        private MethodInfo _setupTwoWayMethod;
        private MethodInfo _executeSettersMethod;
        private MethodInfo _cleanupMethod;
        private object _boundData;
        private bool _autoRefresh;

        void Awake()
        {
            ResolveBindings();
        }

        private void ResolveBindings()
        {
            _pocoType = FindPocoType();
            if (_pocoType == null)
            {
                throw new SimpleUIException(
                    $"[SimpleUI] Could not find POCO type. Please set pocoTypeName in Inspector or ensure a script with UpdateUI() method exists on this GameObject.");
            }

            var config = _pocoType.GetCustomAttribute<SimpleUIAttribute>();
            if (config == null)
            {
                config = new SimpleUIAttribute();
                Debug.Log($"[SimpleUI] No [SimpleUI] attribute on {_pocoType.Name}, using defaults");
            }

            _autoRefresh = config.AutoRefresh;

            var resolver = new BindingResolver(transform, config);
            var resolveMethod = typeof(BindingResolver).GetMethod("Resolve").MakeGenericMethod(_pocoType);
            _cache = resolveMethod.Invoke(resolver, null);

            var cacheType = typeof(BindingCache<>).MakeGenericType(_pocoType);
            var reportsProperty = cacheType.GetProperty("Reports");
            _reports = (BindingReport[])reportsProperty.GetValue(_cache);

            var hasFailuresProperty = cacheType.GetProperty("HasFailures");
            bool hasFailures = (bool)hasFailuresProperty.GetValue(_cache);

            if (hasFailures)
            {
                var getErrorSummaryMethod = cacheType.GetMethod("GetErrorSummary");
                string errorSummary = (string)getErrorSummaryMethod.Invoke(_cache, null);
                throw new SimpleUIException($"SimpleUI binding failed for {_pocoType.Name}:\n{errorSummary}");
            }

            _setupTwoWayMethod = cacheType.GetMethod("SetupTwoWayBindings");
            _executeSettersMethod = cacheType.GetMethod("ExecuteSetters");
            _cleanupMethod = cacheType.GetMethod("Cleanup");

            LogBindingReport();
        }

        private Type FindPocoType()
        {
            if (!string.IsNullOrEmpty(pocoTypeName))
            {
                var type = Type.GetType(pocoTypeName);
                if (type != null) return type;
                
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(pocoTypeName);
                    if (type != null) return type;
                }
            }

            var components = GetComponents<MonoBehaviour>();
            foreach (var component in components)
            {
                if (component == this) continue;
                
                var componentType = component.GetType();
                var updateMethod = componentType.GetMethod("UpdateUI", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (updateMethod != null)
                {
                    var parameters = updateMethod.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType.IsClass)
                    {
                        pocoTypeName = parameters[0].ParameterType.AssemblyQualifiedName;
                        return parameters[0].ParameterType;
                    }
                }
            }

            return null;
        }

        void Update()
        {
            if (_autoRefresh && _boundData != null)
            {
                _executeSettersMethod.Invoke(_cache, new[] { _boundData });
            }
        }

        public void UpdateUI(object data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (!_pocoType.IsInstanceOfType(data))
            {
                throw new ArgumentException($"Data must be of type {_pocoType.Name}, but got {data.GetType().Name}");
            }

            _boundData = data;
            
            if (data is SimpleUIPoco poco)
            {
                poco.BindToView(this);
            }
            
            if (!_twoWayBindingsSetup)
            {
                _setupTwoWayMethod.Invoke(_cache, new[] { data });
                _twoWayBindingsSetup = true;
            }
            
            _executeSettersMethod.Invoke(_cache, new[] { data });
        }

        public void UpdateUI()
        {
            if (_boundData == null)
            {
                throw new InvalidOperationException($"[SimpleUI] Cannot call UpdateUI() without parameters before binding data. Call UpdateUI(data) first to bind a POCO instance.");
            }
            
            _executeSettersMethod.Invoke(_cache, new[] { _boundData });
        }

        void OnDestroy()
        {
            _cleanupMethod?.Invoke(_cache, null);
        }

        private void LogBindingReport()
        {
#if UNITY_EDITOR
            var cacheType = typeof(BindingCache<>).MakeGenericType(_pocoType);
            var getSummaryMethod = cacheType.GetMethod("GetBindingSummary");
            string summary = (string)getSummaryMethod.Invoke(_cache, null);
            Debug.Log($"[SimpleUI] {_pocoType.Name} binding report:\n{summary}");
#endif
        }

#if UNITY_EDITOR
        public BindingReport[] GetReports() => _reports;
        public Type GetPocoType() => _pocoType;
        public object GetBoundData() => _boundData;
#endif
    }
}
