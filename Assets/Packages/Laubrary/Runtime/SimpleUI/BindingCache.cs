using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SimpleUI
{
    public class BindingCache<T> where T : class
    {
        public SimpleUIAttribute Config { get; private set; }
        public BindingReport[] Reports { get; private set; }
        public bool HasFailures { get; private set; }

        private List<BindingEntry> _bindings = new List<BindingEntry>();
        private List<Action> _cleanupActions = new List<Action>();
        private T _currentPoco;

        public BindingCache(SimpleUIAttribute config, BindingReport[] reports)
        {
            Config = config;
            Reports = reports;
            
            foreach (var report in reports)
            {
                if (report.Status == BindingStatus.Error)
                {
                    HasFailures = true;
                    break;
                }
            }
        }

        public void AddBinding(BindingEntry entry)
        {
            _bindings.Add(entry);
        }

        public void AddCleanupAction(Action action)
        {
            _cleanupActions.Add(action);
        }

        public void CopyBindingsFrom(BindingCache<T> source)
        {
            _bindings.AddRange(source._bindings);
            _cleanupActions.AddRange(source._cleanupActions);
        }

        public void SetupTwoWayBindings(T poco)
        {
            _currentPoco = poco;
            foreach (var binding in _bindings)
            {
                binding.SetupTwoWay?.Invoke(poco);
            }
        }

        public void ExecuteSetters(T poco)
        {
            _currentPoco = poco;
            foreach (var binding in _bindings)
            {
                try
                {
                    binding.UpdateUI(poco);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SimpleUI] Error updating UI for field '{binding.FieldName}': {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public void Cleanup()
        {
            foreach (var cleanup in _cleanupActions)
            {
                try
                {
                    cleanup?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SimpleUI] Error during cleanup: {ex.Message}");
                }
            }
            _cleanupActions.Clear();
        }

        public string GetErrorSummary()
        {
            var errors = new List<string>();
            foreach (var report in Reports)
            {
                if (report.Status == BindingStatus.Error)
                {
                    errors.Add($"✗ {report.FieldName}: {report.ErrorMessage}");
                    if (report.SuggestedFixes != null && report.SuggestedFixes.Length > 0)
                    {
                        foreach (var fix in report.SuggestedFixes)
                        {
                            errors.Add($"  → {fix}");
                        }
                    }
                }
            }
            return string.Join("\n", errors);
        }

        public string GetBindingSummary()
        {
            if (Reports == null || Reports.Length == 0)
            {
                return "No binding reports available.";
            }

            var summary = new List<string>();
            int successCount = 0;
            int errorCount = 0;

            foreach (var report in Reports)
            {
                if (report == null) continue;

                if (report.Status == BindingStatus.Success)
                {
                    successCount++;
                    string componentName = report.ComponentType?.Name ?? "Unknown";
                    string path = report.GameObjectPath ?? "Unknown";
                    summary.Add($"✓ {report.FieldName} → {path} ({componentName}) [{report.Confidence}]");
                }
                else
                {
                    errorCount++;
                    summary.Add($"✗ {report.FieldName}: {report.ErrorMessage ?? "Unknown error"}");
                }
            }

            summary.Insert(0, $"Summary: {successCount} Success, {errorCount} Errors");
            return string.Join("\n", summary);
        }

        public class BindingEntry
        {
            public string FieldName;
            public Action<T> UpdateUI;
            public Action<T> SetupTwoWay;
        }
    }
}
