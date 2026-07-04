using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
#pragma warning disable CS0618 // Type or member is obsolete
#if UNITY_6000_2_OR_NEWER
using BaseTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using BaseTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace AssetInventory
{
    public class HideContentUI : BasicEditorUI
    {
        private AssetInfo _info;
        private string _displayName;
        private bool _initialized;

        private FileTreeViewControl _treeView;
        private BaseTreeViewState _treeViewState;
        private Dictionary<string, FileTreeElement> _pathToElementMap;

        private string _exclusionRules = "";
        private List<string> _rules = new List<string>();
        private string _newRule = "";
        private float _splitterPos = 0.55f;
        private Vector2 _rulesScrollPos;

        public static HideContentUI ShowWindow()
        {
            HideContentUI window = GetWindow<HideContentUI>("Hide Package Content");
            window.minSize = new Vector2(700, 450);
            return window;
        }

        public void Init(AssetInfo info)
        {
            _info = info;
            _displayName = info.GetDisplayName();
            _initialized = false;
            BuildTree();
        }

        private void BuildTree()
        {
            List<AssetFile> files = DBAdapter.DB.Query<AssetFile>("SELECT * FROM AssetFile WHERE AssetId=?", _info.AssetId);
            if (files.Count == 0)
            {
                _initialized = true;
                return;
            }

            if (_treeViewState == null) _treeViewState = new BaseTreeViewState();

            FileTreeBuilder.Result result = FileTreeBuilder.Build(files, _treeViewState);
            _treeView = result.TreeView;
            _pathToElementMap = result.PathToElementMap;

            // Load current hidden state: deselect hidden files
            HashSet<string> hiddenPaths = new HashSet<string>(
                files.Where(f => f.Hidden).Select(f => f.Path));

            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (hiddenPaths.Contains(kvp.Key))
                {
                    kvp.Value.IsSelected = false;
                }
            }

            // Load exclusion rules from metadata
            LoadExclusionRules();

            // Apply pattern markings
            ApplyPatternMarkings();

            _initialized = true;
        }

        private void LoadExclusionRules()
        {
            Dictionary<int, List<string>> hidePatterns = Metadata.GetHidePatterns();
            if (hidePatterns.TryGetValue(_info.AssetId, out List<string> patterns))
            {
                _rules = new List<string>(patterns);
            }
            else
            {
                _rules = new List<string>();
            }
            SyncRulesString();
        }

        private void SyncRulesString()
        {
            _exclusionRules = string.Join("\n", _rules);
        }

        private void ApplyPatternMarkings()
        {
            if (_pathToElementMap == null) return;

            // Reset auto-exclusion state
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                kvp.Value.IsAutoExcluded = false;
            }

            foreach (string pattern in _rules)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
                {
                    if (kvp.Value.IsFolder) continue;
                    if (MatchesPattern(kvp.Key, pattern))
                    {
                        kvp.Value.IsAutoExcluded = true;
                        kvp.Value.IsSelected = false;
                    }
                }
            }
        }

        private bool IsMatchedByAnyRule(string path)
        {
            foreach (string pattern in _rules)
            {
                if (!string.IsNullOrWhiteSpace(pattern) && MatchesPattern(path, pattern)) return true;
            }
            return false;
        }

        private List<string> GetManuallyHiddenPaths()
        {
            if (_pathToElementMap == null) return new List<string>();

            // Collect all manually deselected file paths (not auto-excluded by rules)
            HashSet<string> manualFiles = new HashSet<string>();
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (kvp.Value.IsFolder) continue;
                if (!kvp.Value.IsSelected && !kvp.Value.IsAutoExcluded)
                {
                    manualFiles.Add(kvp.Key);
                }
            }

            if (manualFiles.Count == 0) return new List<string>();

            // Group files by folder and check if entire folders are deselected
            // Build folder → files mapping for non-auto-excluded files
            Dictionary<string, List<string>> folderToAllFiles = new Dictionary<string, List<string>>();
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (kvp.Value.IsFolder || kvp.Value.IsAutoExcluded) continue;

                string folder = GetParentFolder(kvp.Key);
                if (folder == null) continue;

                if (!folderToAllFiles.TryGetValue(folder, out List<string> list))
                {
                    list = new List<string>();
                    folderToAllFiles[folder] = list;
                }
                list.Add(kvp.Key);
            }

            // Find folders where all non-auto-excluded files are manually deselected
            // Only collapse if subfolders also have no selected files
            HashSet<string> collapsedFolders = new HashSet<string>();
            foreach (KeyValuePair<string, List<string>> kvp in folderToAllFiles)
            {
                if (!kvp.Value.All(f => manualFiles.Contains(f))) continue;

                bool hasSelectedSubFiles = folderToAllFiles.Any(other =>
                    other.Key != kvp.Key && other.Key.StartsWith(kvp.Key + "/") &&
                    other.Value.Any(f => !manualFiles.Contains(f)));
                if (!hasSelectedSubFiles)
                {
                    collapsedFolders.Add(kvp.Key);
                }
            }

            // Collapse upward: if all child folders of a parent are collapsed, collapse the parent
            bool changed = true;
            while (changed)
            {
                changed = false;
                Dictionary<string, List<string>> parentToChildren = new Dictionary<string, List<string>>();
                foreach (string folder in collapsedFolders)
                {
                    string parent = GetParentFolder(folder);
                    if (parent == null) continue;

                    if (!parentToChildren.TryGetValue(parent, out List<string> children))
                    {
                        children = new List<string>();
                        parentToChildren[parent] = children;
                    }
                    children.Add(folder);
                }

                foreach (KeyValuePair<string, List<string>> kvp in parentToChildren)
                {
                    // Check parent has no direct non-auto-excluded files outside collapsed children
                    bool hasUncoveredFiles = folderToAllFiles.TryGetValue(kvp.Key, out List<string> directFiles)
                        && directFiles.Any(f => !manualFiles.Contains(f));
                    if (hasUncoveredFiles) continue;

                    // Check all child folders of this parent are collapsed
                    List<string> allChildFolders = collapsedFolders.Where(f => GetParentFolder(f) == kvp.Key).ToList();
                    // Also check there are no selected child folders with files
                    bool allChildrenCollapsed = folderToAllFiles.Keys
                        .Where(f => GetParentFolder(f) == kvp.Key)
                        .All(f => collapsedFolders.Contains(f));

                    if (allChildrenCollapsed && allChildFolders.Count > 0)
                    {
                        collapsedFolders.Add(kvp.Key);
                        foreach (string child in allChildFolders)
                        {
                            collapsedFolders.Remove(child);
                        }
                        changed = true;
                        break;
                    }
                }
            }

            // Build result: collapsed folders + individual files not covered by collapsed folders
            List<string> result = new List<string>();
            foreach (string folder in collapsedFolders.OrderBy(f => f))
            {
                result.Add(folder + "/");
            }
            foreach (string file in manualFiles.OrderBy(f => f))
            {
                bool coveredByFolder = collapsedFolders.Any(f => file.StartsWith(f + "/") || file.StartsWith(f));
                if (!coveredByFolder)
                {
                    result.Add(file);
                }
            }
            return result;
        }

        private static string GetParentFolder(string path)
        {
            int lastSlash = path.LastIndexOf('/');
            return lastSlash > 0 ? path.Substring(0, lastSlash) : null;
        }

        private void ReactivateAfterRuleChange()
        {
            // Re-select files that are no longer matched by any rule and were auto-excluded
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (kvp.Value.IsFolder) continue;
                if (kvp.Value.IsAutoExcluded && !IsMatchedByAnyRule(kvp.Key))
                {
                    kvp.Value.IsSelected = true;
                }
            }
            ApplyPatternMarkings();
        }

        private bool MatchesPattern(string path, string pattern)
        {
            if (pattern.StartsWith("*"))
            {
                string ext = pattern.TrimStart('*').TrimStart('.');
                if (string.IsNullOrEmpty(ext)) return false;

                string fileExt = System.IO.Path.GetExtension(path);
                if (!string.IsNullOrEmpty(fileExt)) fileExt = fileExt.TrimStart('.');
                return string.Equals(fileExt, ext, System.StringComparison.OrdinalIgnoreCase);
            }

            if (pattern.Contains("/"))
            {
                return path.Contains(pattern);
            }

            // Folder name match
            return path.Contains("/" + pattern + "/") || path.StartsWith(pattern + "/");
        }

        public override void OnGUI()
        {
            base.OnGUI();

            if (_info == null)
            {
                EditorGUILayout.HelpBox("No package selected.", MessageType.Info);
                return;
            }

            if (!_initialized)
            {
                EditorGUILayout.HelpBox("Loading...", MessageType.Info);
                return;
            }

            if (_treeView == null)
            {
                EditorGUILayout.HelpBox("No indexed files found for this package.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Package: {_displayName}", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Deselect files to hide them from search results. Exclusion rules on the right will automatically hide matching files.", MessageType.Info);

            EditorGUILayout.Space();

            float leftWidth = Mathf.Floor(position.width * _splitterPos);

            // Horizontal split
            GUILayout.BeginHorizontal();

            // Left pane: file tree
            GUILayout.BeginVertical(GUILayout.Width(leftWidth));
            EditorGUILayout.LabelField("Files", EditorStyles.boldLabel);
            Rect treeRect = GUILayoutUtility.GetRect(0, 10000, 0, 10000);
            _treeView.OnGUI(treeRect);
            GUILayout.EndVertical();

            // Splitter
            int splitterControlId = GUIUtility.GetControlID(FocusType.Passive);
            Rect splitterRect = GUILayoutUtility.GetRect(4, 4, 0, 10000, GUILayout.Width(4));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
            {
                GUIUtility.hotControl = splitterControlId;
                Event.current.Use();
            }
            if (GUIUtility.hotControl == splitterControlId)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _splitterPos = Mathf.Clamp(Event.current.mousePosition.x / position.width, 0.2f, 0.8f);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0;
                    Event.current.Use();
                }
            }

            // Right pane: exclusion rules + manually hidden
            GUILayout.BeginVertical();

            EditorGUILayout.LabelField("Exclusion Rules", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Patterns: *.ext, folder, path/segment", EditorStyles.miniLabel);

            _rulesScrollPos = EditorGUILayout.BeginScrollView(_rulesScrollPos);

            int deleteIdx = -1;
            for (int i = 0; i < _rules.Count; i++)
            {
                GUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                _rules[i] = EditorGUILayout.TextField(_rules[i]);
                if (EditorGUI.EndChangeCheck())
                {
                    SyncRulesString();
                    ApplyPatternMarkings();
                }
                if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Remove rule"), GUILayout.Width(28), GUILayout.Height(18)))
                {
                    deleteIdx = i;
                }
                GUILayout.EndHorizontal();
            }

            if (deleteIdx >= 0)
            {
                _rules.RemoveAt(deleteIdx);
                SyncRulesString();
                ReactivateAfterRuleChange();
            }

            // Add new rule
            bool addRule = false;

            // Check Return key BEFORE TextField consumes the event
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "NewRuleField"
                && !string.IsNullOrWhiteSpace(_newRule))
            {
                addRule = true;
                Event.current.Use();
            }

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("NewRuleField");
            _newRule = EditorGUILayout.TextField(_newRule);
            if (GUILayout.Button("Add", GUILayout.Width(40)) && !string.IsNullOrWhiteSpace(_newRule))
            {
                addRule = true;
            }
            GUILayout.EndHorizontal();

            if (addRule)
            {
                _rules.Add(_newRule.Trim());
                _newRule = "";
                SyncRulesString();
                ApplyPatternMarkings();
                GUI.FocusControl(null);
            }

            // Manually hidden section
            List<string> manualPaths = GetManuallyHiddenPaths();
            if (manualPaths.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Manually Hidden ({manualPaths.Count})", EditorStyles.boldLabel);
                EditorGUI.BeginDisabledGroup(true);
                foreach (string path in manualPaths)
                {
                    EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                }
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            // Bottom buttons
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply", CommonUIStyles.mainButton, GUILayout.Width(120), GUILayout.Height(30)))
            {
                ApplyChanges();
            }
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(30)))
            {
                Close();
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void ApplyChanges()
        {
            // Reset all files to not hidden, then set hidden based on tree selection
            DBAdapter.DB.Execute("UPDATE AssetFile SET Hidden=0 WHERE AssetId=?", _info.AssetId);

            List<int> hiddenIds = new List<int>();
            List<AssetFile> files = DBAdapter.DB.Query<AssetFile>("SELECT Id, Path FROM AssetFile WHERE AssetId=?", _info.AssetId);
            foreach (AssetFile file in files)
            {
                if (_pathToElementMap.TryGetValue(file.Path, out FileTreeElement element))
                {
                    if (!element.IsSelected)
                    {
                        hiddenIds.Add(file.Id);
                    }
                }
            }

            if (hiddenIds.Count > 0)
            {
                Assets.SetFilesHidden(hiddenIds, true);
            }

            // Save exclusion rules to metadata
            SaveExclusionRules();

            // Apply patterns to set Hidden flag on matching files
            Assets.ApplyHidePatterns(_info.AssetId);

            Close();
        }

        private void SaveExclusionRules()
        {
            string trimmedRules = string.Join(";", _rules
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s)));

            // Find or create the Hide metadata assignment
            List<MetadataInfo> metadata = Metadata.GetPackageMetadata(_info.AssetId);
            MetadataInfo hideMetadata = metadata?.FirstOrDefault(m => m.Name == MetadataDefinition.FIELD_HIDE);

            if (string.IsNullOrEmpty(trimmedRules))
            {
                if (hideMetadata != null)
                {
                    Metadata.RemoveAssignment(_info, hideMetadata);
                }
            }
            else
            {
                if (hideMetadata != null)
                {
                    hideMetadata.StringValue = trimmedRules;
                    DBAdapter.DB.Update(hideMetadata.ToAssignment());
                }
                else
                {
                    // Find the Hide definition
                    List<MetadataDefinition> defs = Metadata.LoadDefinitions();
                    MetadataDefinition hideDef = defs.FirstOrDefault(d => d.Name == MetadataDefinition.FIELD_HIDE);
                    if (hideDef != null)
                    {
                        Metadata.AddAssignment(_info, hideDef.Id, MetadataAssignment.Target.Package);
                        // Reload and set value
                        metadata = Metadata.GetPackageMetadata(_info.AssetId);
                        hideMetadata = metadata?.FirstOrDefault(m => m.Name == MetadataDefinition.FIELD_HIDE);
                        if (hideMetadata != null)
                        {
                            hideMetadata.StringValue = trimmedRules;
                            DBAdapter.DB.Update(hideMetadata.ToAssignment());
                        }
                    }
                }
            }
        }
    }
}
