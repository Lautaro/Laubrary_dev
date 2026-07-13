using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class MenuItemNode {
        public GUIContent content;
        public GenericMenu.MenuFunction func;
        public GenericMenu.MenuFunction2 func2;
        public object userData;
        public bool separator;
        public bool on;

        public string name { get; }
        public MenuItemNode parent { get; }

        public List<MenuItemNode> Nodes { get; private set; }

        public MenuItemNode(string p_name = "", MenuItemNode p_parent = null) {
            name = p_name;
            parent = p_parent;
            Nodes = new List<MenuItemNode>();
        }

        public MenuItemNode CreateNode(string p_name) {
            var node = new MenuItemNode(p_name, this);
            Nodes.Add(node);
            return node;
        }

        // TODO Optimize
        public MenuItemNode GetOrCreateNode(string p_name) {
            var node = Nodes.Find(n => n.name == p_name);
            if (node == null) {
                node = CreateNode(p_name);
}

            return node;
        }

        public List<MenuItemNode> Search(string p_search, bool includeFolders = false) {
            var lowerSearch = (p_search ?? "").ToLower();
            List<MenuItemNode> result = new List<MenuItemNode>();
            
            string[] searchSplits = ObjectNames.NicifyVariableName(lowerSearch).ToLower().Split(' ');

            foreach (var node in Nodes) {

                if (node.Nodes.Count == 0 || includeFolders) {
                    bool found = string.IsNullOrEmpty(lowerSearch) || node.name.ToLower().Contains(lowerSearch);
                    if (!found && !string.IsNullOrEmpty(lowerSearch)) {
                        found = node.name.Replace(" ", "").ToLower().Contains(lowerSearch);
                    }
                    if (!found && !string.IsNullOrEmpty(lowerSearch)) {
                        string nicifyLowerName = ObjectNames.NicifyVariableName(node.name).ToLower();
                        found = true;
                        for (int i = 0; i < searchSplits.Length; i++) {
                            if (searchSplits[i] == "") continue;
                            if (!nicifyLowerName.Contains(searchSplits[i])) {
                                found = false;
                                break;
                              }
                        }
                    }
                    if (found) {
                        result.Add(node);
                    }
                }

                result.AddRange(node.Search(p_search, includeFolders));
            }

            return result;
        }

        public string GetPath() {
            return parent == null ? "" : parent.GetPath() + "/" + name;
        }

        public void Execute() {
            if (func != null) {
                func?.Invoke();
            }
            else {
                func2?.Invoke(userData);
            }
        }

        public void ExecuteWithSelectionStatus(bool _selected) {
            func2?.Invoke(_selected);
        }
    }

    public class GenericMenuPopup : PopupWindowContent {

        protected bool showQuickBar {
            get => EditorPrefs.GetBool("ShowQuickBar", true);
            set => EditorPrefs.SetBool("ShowQuickBar", value);
        }

        private float _customFilterHeight = 44f; // non-zero default prevents overlap on first frame before Repaint measures the real height

        private bool doubleClicked;

        public System.Action<string> onSearchTermChanged;
        public System.Action<object> onRightClicked;

        public List<ZoundsEditorPresets.NameListPreset> presetList;
        public string lastSelectedPresetName;
        private Vector2 presetScrollPos;
        private ZoundsEditorPresets.NameListPreset presetToRename;

        public static GenericMenuPopup Get(GenericMenu p_menu, string p_title) {
            var popup = new GenericMenuPopup(p_menu, p_title, null);
            return popup;
        }

        public static GenericMenuPopup Show(GenericMenu p_menu, string p_title, Vector2 p_position, List<string> starredPaths, 
            string _searchTerm = "", System.Action<string> _onSearchTermChanged = null, 
            System.Action<object> _onRightClicked = null, int _columnCount = 3, bool _invokeNoneSelected = false,
            List<ZoundsEditorPresets.NameListPreset> presetList = null,
            System.Action<System.Action<string, bool>> _onDrawCustomFilter = null) {
            
            var popup = new GenericMenuPopup(p_menu, p_title, starredPaths, _columnCount, _invokeNoneSelected, _onDrawCustomFilter);
            popup.onSearchTermChanged = _onSearchTermChanged;
            popup._search = _searchTerm;
            popup.resizeToContent = false;
            popup.onRightClicked = _onRightClicked;
            popup.presetList = presetList;
            popup.lastSelectedPresetName = null;
            popup.isResizable = false;

            if (p_title != null && p_title.Contains("Add New Klip")) {
                popup._folderFilter = ""; // Ensure starting in All mode
            }

            // Convert GUI position to screen position for the EditorWindow
            Vector2 screenPos = GUIUtility.GUIToScreenPoint(p_position);
            GenericMenuEditorWindow.Show(p_menu, p_title, screenPos, starredPaths,
                _searchTerm, _onSearchTermChanged, _onRightClicked, _columnCount, _invokeNoneSelected,
                presetList, _onDrawCustomFilter);

            return popup;
        }

        public System.Action<System.Action<string, bool>> onDrawCustomFilter;

        private static GUIStyle _labelWhite;
        private static GUIStyle LabelWhite {
            get {
                if (_labelWhite == null) {
                    _labelWhite = new GUIStyle("label");
                    _labelWhite.normal.textColor = Color.white;
                }
                return _labelWhite;
            }
        }

        private static GUIStyle _backStyle;
        public static GUIStyle BackStyle {
            get {
                if (_backStyle == null) {
                    _backStyle = new GUIStyle(GUI.skin.button);
                    _backStyle.alignment = TextAnchor.MiddleLeft;
                    _backStyle.hover.background = Texture2D.grayTexture;
                    _backStyle.normal.textColor = Color.black;
                }

                return _backStyle;
            }
        }

        private static GUIStyle _plusStyle;
        public static GUIStyle PlusStyle {
            get {
                if (_plusStyle == null) {
                    _plusStyle = new GUIStyle();
                    _plusStyle.fontStyle = FontStyle.Bold;
                    _plusStyle.normal.textColor = Color.white;
                    _plusStyle.fontSize = 16;
                }

                return _plusStyle;
            }
        }

        private static GUIContent tempGUIContent = new GUIContent();

        private bool selectedNodesNeedRecache;
        protected HashSet<MenuItemNode> selectedNodes = new HashSet<MenuItemNode>();
        private IOrderedEnumerable<MenuItemNode> selectedNodesOrdered;
        private Vector2 quickBarScrollPos;

        private string _title;
        private Vector2 _scrollPosition;
        private MenuItemNode _rootNode;
        private MenuItemNode _currentNode;
        private MenuItemNode _hoverNode;
        private int hoveredIndex;
        public string _search;
        public string _folderFilter;
        private bool _repaint = false;
        private int _contentHeight;
        private bool _useScroll;
        private List<MenuItemNode> _starredNodes;
        public bool WantsToClose { get; private set; } = false;

        private int columnCount = 3;
        private bool invokeNoneSelected = false;
        public int width = 350; // dynamically set
        public int height = 400;
        public int maxHeight = 800;
        public bool resizeToContent = false;
        public bool isResizable = true;
        private bool isResizing = false;
        private Vector2 minSize = new Vector2(350, 250);
        private Vector2 maxSize = new Vector2(1000, 800);
        public bool showOnStatus = true;
        public bool showSearch = true;
        public bool showTooltip = false;
        public bool showTitle = false;

        private int _effectiveColumnCount = 1; // recalculated each frame from window width
        private float _frameWidth = 350f;       // p_rect.width captured each OnGUI frame

        private List<MenuItemNode> _cachedSearchResults = null;
        private string _cachedSearchTerm = null;
        private string _cachedFolderFilter = null;

        public GenericMenuPopup(GenericMenu p_menu, string p_title, List<string> p_starredPaths, int p_columnCount = 3, bool p_invokeNoneSelected = false, System.Action<System.Action<string, bool>> p_onDrawCustomFilter = null) {
            columnCount = p_columnCount;
            invokeNoneSelected = p_invokeNoneSelected;
            _title = p_title;
            onDrawCustomFilter = p_onDrawCustomFilter;
            showTitle = !string.IsNullOrWhiteSpace(_title);
            _currentNode = _rootNode = GenerateMenuItemNodeTree(p_menu, out float columnWidth);
            width = Mathf.CeilToInt(columnWidth * columnCount + 30);
            if (p_starredPaths != null) {
                _starredNodes = new List<MenuItemNode>();
                for (int i = 0; i < p_starredPaths.Count; i++) {
                    string[] split = p_starredPaths[i].Split('/');
                    List<MenuItemNode> items = _currentNode.Search(split[split.Length - 1]);
                    foreach (MenuItemNode item in items) {
                        if (item.name == split[split.Length - 1]) {
                            _starredNodes.Add(item);
                        }
                    }
                }
            }
        }

        public override Vector2 GetWindowSize() {
            if (isResizing) {
                return new Vector2(width, height);
            }
            if (resizeToContent) {
                height = Mathf.Clamp(_contentHeight + 20, (int)minSize.y, maxHeight);
                return new Vector2(width, height);
            }
            return new Vector2(width, height);
        }

        public void Show(float p_x, float p_y) {
            PopupWindow.Show(new Rect(p_x, p_y, 0, 0), this);
        }

        public void Show(Vector2 p_position) {
            PopupWindow.Show(new Rect(p_position.x, p_position.y, 0, 0), this);
        }

        public override void OnGUI(Rect p_rect) {
            HandleResize(p_rect);

            // Capture actual rendered width this frame — always use this for layout, never the stale 'width' field
            _frameWidth = p_rect.width;

            // Calculate how many columns fit in the current window width.
            // Each column needs at least ItemMinWidth pixels. Never exceed the configured columnCount.
            const float ItemMinWidth = 120f;
            int maxFit = Mathf.Max(1, Mathf.FloorToInt(_frameWidth / ItemMinWidth));
            _effectiveColumnCount = columnCount == 1 ? 1 : Mathf.Min(columnCount, maxFit);

            if (Event.current.type == EventType.Layout) {
                _useScroll = _contentHeight > maxHeight || (!resizeToContent && _contentHeight > height);
                _repaint = false;
            }
            
            if (_repaint) {
                _repaint = false;
                if (editorWindow != null) {
                    editorWindow.Repaint();
                }
            }

            _contentHeight = 0;
            GUIStyle style = new GUIStyle();
            style.normal.background = Texture2D.whiteTexture;
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 1);
            //GUI.Box(p_rect, string.Empty, style);
            GUI.color = Color.white;

            float yOffset = 0f;

            if (showTitle) {
                DrawTitle(new Rect(p_rect.x, p_rect.y, p_rect.width, 24));
                yOffset += 24f;
            }

            if (presetList != null) {
                float totalPresetsWidth = 0f;
                tempGUIContent.text = "Default";
                float width = EditorStyles.helpBox.CalcSize(tempGUIContent).x;
                totalPresetsWidth += width;
                foreach (var preset in presetList) {
                    tempGUIContent.text = preset.name;
                    width = EditorStyles.toolbarButton.CalcSize(tempGUIContent).x;
                    totalPresetsWidth += width;
                }

                float presetsHeight =
                    totalPresetsWidth > (p_rect.width - PresetsBarDrawer.savePresetButtonWidthMinimal - 4f) ?
                    32f : 20f;

                var presetsRect = new Rect(p_rect.x, p_rect.y + yOffset, p_rect.width, presetsHeight);
                presetScrollPos = PresetsBarDrawer.DrawPresetsMinimal(presetScrollPos, presetsRect, presetList, totalPresetsWidth, lastSelectedPresetName, ClearPresetToRename, SavePreset, HandlePresetElementClick);
                yOffset += presetsHeight;
            }

            if (showSearch) {
                // First, draw custom filter (folder bar) which uses GUILayout
                // This will push down subsequent GUILayout elements but we need to track yOffset for the absolute Rects
                if (onDrawCustomFilter != null) {
                    // Start a GUILayout area at the current yOffset so drawing doesn't happen at 0,0
                    GUILayout.BeginArea(new Rect(p_rect.x, p_rect.y + yOffset, p_rect.width, 350f));

                    // Wrap in a vertical group so we can reliably measure the rendered height
                    // via GUILayoutUtility.GetLastRect() on the group itself.
                    GUILayout.BeginVertical();
                    
                    // Force a minimum height so it's always visible even if empty
                    GUILayout.Space(2); 

                    onDrawCustomFilter.Invoke((newSearch, isFolder) => {
                        if (isFolder) {
                            _folderFilter = newSearch;
                            _cachedSearchResults = null;
                            _repaint = true;
                        }
                        else {
                            _search = newSearch;
                            _cachedSearchResults = null;
                            onSearchTermChanged?.Invoke(newSearch);
                            _repaint = true;
                        }
                    });

                    GUILayout.EndVertical();

                    // GetLastRect here measures the vertical group we just closed — always valid.
                    if (Event.current.type == EventType.Repaint) {
                        _customFilterHeight = GUILayoutUtility.GetLastRect().height;
                    }

                    GUILayout.EndArea();

                    yOffset += _customFilterHeight;
                }

                var searchRect = new Rect(p_rect.x, p_rect.y + yOffset, p_rect.width, 20f);
                float customTogglesWidth = OnDrawCustomToggles(searchRect);
                searchRect.width -= (85f + customTogglesWidth);
                DrawSearch(searchRect);

                var quickBarRect = new Rect(searchRect.xMax + 5f, searchRect.y, 80f, searchRect.height);
                EditorGUI.BeginChangeCheck();
                var showQuickBarTemp = EditorGUI.ToggleLeft(quickBarRect, "Quick Bar", showQuickBar);
                if (EditorGUI.EndChangeCheck()) {
                    showQuickBar = showQuickBarTemp;
                }

                yOffset += 22f;
            }

            if (showQuickBar) {
                var viewportRect = new Rect(p_rect.x, p_rect.y + yOffset, p_rect.width, 36f);
                
                DrawQuickBar(viewportRect);

                yOffset += 36f;
            }

            DrawMenuItems(new Rect(p_rect.x, p_rect.y + yOffset, p_rect.width, p_rect.height - (showTooltip ? 80 : 0) - yOffset - 20));
            GUI.color = Color.white;

            if (showTooltip) {
                DrawTooltip(new Rect(p_rect.x + 5, p_rect.y + p_rect.height - 78, p_rect.width - 10, 36));
            }

            if (resizeToContent && !isResizing) {
                _contentHeight += 10;
                // Height is now calculated in GetWindowSize(), don't set it here
            }
            EditorGUI.FocusTextInControl("Search");

            if (doubleClicked) {
                // check double click validity
                if (!invokeNoneSelected && selectedNodes.Count == 0)
                    doubleClicked = false;
            }

            if (doubleClicked || GUI.Button(new Rect(p_rect.x, p_rect.y + p_rect.height - 20, p_rect.width, 20), invokeNoneSelected? "Update" : "Add Selected Items")) {
                if (invokeNoneSelected) {
                    InvokeWithSelectionStatusRecursive(_rootNode);
                }
                else {
                    var sortedSelected = selectedNodes.OrderBy(_node => _node.content.text);
                    foreach (var node in sortedSelected) {
                        node.Execute();
                    }
                }
                WantsToClose = true;
                if (editorWindow != null) base.editorWindow.Close();
            }
        }

        private void ClearPresetToRename() {
            presetToRename = null;
        }

        private void SavePreset(string presetName) {
            var zoundsPresets = ZoundsEditorPresets.Instance;
            Undo.RecordObject(zoundsPresets, "save preset");
            ZoundsEditorPresets.NameListPreset preset;
            if (presetToRename == null) {
                preset = presetList.Find(p => p.name == presetName);
                if (preset == null) {
                    preset = new ZoundsEditorPresets.NameListPreset() {
                        name = presetName
                    };
                    presetList.Add(preset);
                }
            }
            else {
                preset = presetToRename;
                preset.name = presetName;
                presetToRename = null;
            }

            var zoundTabProperties = ZoundsWindowProperties.Instance.zoundTabProperties[0];
            preset.names = selectedNodes.Select(n => n.name).ToList();

            lastSelectedPresetName = preset.name;
            EditorUtility.SetDirty(zoundsPresets);
        }

        private void HandlePresetElementClick(string presetName) {
            var evt = Event.current;
            var mousePosInScreen = GUIUtility.GUIToScreenPoint(evt.mousePosition);
            var preset = presetList.Find(p => p.name == presetName);

            if (evt.button == 0) {
                selectedNodes.Clear();
                if (preset != null) {
                    SelectPresetNodesRecursive(_rootNode, preset.names);
                    lastSelectedPresetName = presetName;
                }
                selectedNodesNeedRecache = true;
            }
            else if (evt.button == 1) {
                if (preset != null) {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Rename"), false, () => {
                        if (preset != null) {
                            presetToRename = preset;
                            SavePresetPopup.Show(GUIUtility.ScreenToGUIPoint(mousePosInScreen), presetName, SavePreset);
                        }
                    });
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Replace with Current Selection"), false, () => {
                        SavePreset(presetName);
                    });
                    menu.AddItem(new GUIContent("Delete"), false, () => {
                        if (EditorUtility.DisplayDialog("Remove Preset: " + presetName, "Are you sure you want to remove this preset?\n" + presetName, "Remove", "Cancel")) {
                            var zoundsPresets = ZoundsEditorPresets.Instance;
                            Undo.RecordObject(zoundsPresets, "delete preset");
                            presetList.Remove(preset);
                            EditorUtility.SetDirty(zoundsPresets);
                        }
                    });
                    menu.ShowAsContext();
                }
            }
        }

        private void SelectPresetNodesRecursive(MenuItemNode parentNode, List<string> selectedNames) {
            foreach (var node in parentNode.Nodes) {
                if (selectedNames.Contains(node.name)) selectedNodes.Add(node);
                SelectPresetNodesRecursive(node, selectedNames);
            }
        }


        protected virtual float OnDrawCustomToggles(Rect searchRect) {
            return 0f;
        }

        private void InvokeWithSelectionStatusRecursive(MenuItemNode _node) {
            bool selected = selectedNodes.Contains(_node);
            //Debug.Log("Invoke: " + _node.name + ": " + selected);
            _node.ExecuteWithSelectionStatus(selected);
            foreach (var childNode in _node.Nodes) {
                InvokeWithSelectionStatusRecursive(childNode);
            }
        }

        private void DrawTitle(Rect p_rect) {
            _contentHeight += 24;
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 12;
            style.alignment = TextAnchor.LowerCenter;
            p_rect.y -= 5;
            GUI.Label(p_rect, _title, style);
        }

        private void DrawSearch(Rect p_rect) {
            _contentHeight += 22;

            List<MenuItemNode> nodes;
            List<MenuItemNode> sortedNodes;
            if ((!string.IsNullOrEmpty(_search)) || (!string.IsNullOrEmpty(_folderFilter)) || (_title != null && _title.Contains("Add New Klip"))) {
                nodes = _rootNode.Search(_search ?? "");
                if (!string.IsNullOrEmpty(_folderFilter)) {
                    nodes = nodes.Where(n => {
                        if (n.Nodes.Count > 0) return false;
                        string path = n.GetPath().ToLower();
                        return path.Contains("/" + _folderFilter.ToLower());
                    }).ToList();
                }
                else {
                    // Show all files recursive in "All" view
                    nodes = nodes.Where(n => n.Nodes.Count == 0).ToList();
                }
                sortedNodes = new List<MenuItemNode>(nodes);
                sortedNodes.Sort((n1, n2) => {
                    string p1 = n1.parent.GetPath();
                    string p2 = n2.parent.GetPath();
                    if (p1 == p2)
                        return n1.name.CompareTo(n2.name);

                    return p1.CompareTo(p2);
                });
            }
            else {
                nodes = _currentNode.Nodes;
                sortedNodes = nodes;
            }

            if (Event.current.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "Search") {
                int currentSearchIndex = sortedNodes.IndexOf(_hoverNode);
                int searchIndex = currentSearchIndex;

                switch (Event.current.keyCode) {
                    case KeyCode.DownArrow:
                        do {
                            searchIndex++;
                            if (searchIndex >= nodes.Count) {
                                searchIndex = 0;
                            }
                            _hoverNode = sortedNodes[searchIndex];
                            if (searchIndex == currentSearchIndex) {
                                break;
                            }
                        } while (_hoverNode.separator);
                        hoveredIndex = nodes.IndexOf(_hoverNode);
                        break;

                    case KeyCode.UpArrow:
                        do {
                            searchIndex--;
                            if (searchIndex < 0) {
                                searchIndex = nodes.Count - 1;
                            }
                            _hoverNode = sortedNodes[searchIndex];
                            if (searchIndex == currentSearchIndex) {
                                break;
                            }
                        } while (_hoverNode.separator);
                        hoveredIndex = nodes.IndexOf(_hoverNode);
                        break;

                    case KeyCode.Return:
                        if (_hoverNode != null) {
                            if (_hoverNode.Nodes.Count == 0) {
                                SelectNode(_hoverNode);
                            }
                            else {
                                hoveredIndex = 0;
                                _currentNode = _hoverNode;
                                if (_currentNode.Nodes.Count > 0) {
                                    _hoverNode = _currentNode.Nodes[hoveredIndex];
                                }
                                _repaint = true;
                            }
                        }
                        break;

                    case KeyCode.Escape:
                        if (_currentNode.parent != null) {
                            _currentNode = _currentNode.parent;
                            hoveredIndex = 0;
                            if (_currentNode.Nodes.Count > 0) {
                                _hoverNode = _currentNode.Nodes[hoveredIndex];
                            }
                            _repaint = true;
                        }
                        break;
                }
            }

            GUI.SetNextControlName("Search");
            string newSearch = GUI.TextField(p_rect, _search);
            if (newSearch != _search) {
                if (newSearch != null & newSearch != "") {
                    nodes = _rootNode.Search(newSearch);
                }
                else {
                    nodes = _currentNode.Nodes;
                    if (_title != null && _title.Contains("Add New Klip")) {
                        nodes = _rootNode.Search("", false).Where(n => n.Nodes.Count == 0).ToList();
                    }
                }

                if (nodes.Count > 0) {
                    _hoverNode = nodes[0];
                    hoveredIndex = 0;
                }
                else {
                    _hoverNode = null;
                }
                _search = newSearch;
                _cachedSearchResults = null;
            }
            onSearchTermChanged?.Invoke(_search);
        }

        private void DrawQuickBar(Rect viewportRect) {
            if (selectedNodesNeedRecache || selectedNodesOrdered == null) {
                selectedNodesNeedRecache = false;
                selectedNodesOrdered = selectedNodes.OrderBy(_node => _node.content.text);
            }

            float closeButtonWidth = 22f;

            float totalWidth = 0f;
            foreach (var node in selectedNodesOrdered) {
                tempGUIContent.text = node.name;
                float width = EditorStyles.helpBox.CalcSize(tempGUIContent).x;
                totalWidth += width + closeButtonWidth;
            }

            var contentRect = new Rect(viewportRect.x, viewportRect.y, totalWidth, 22f);

            var guiColor = GUI.color;
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(viewportRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            quickBarScrollPos = GUI.BeginScrollView(viewportRect, quickBarScrollPos, contentRect);
            {
                float currentX = 0f;
                foreach (var node in selectedNodesOrdered) {
                    tempGUIContent.text = node.name;
                    float width = EditorStyles.helpBox.CalcSize(tempGUIContent).x;
                    var elmRect = new Rect(currentX, contentRect.y, width + closeButtonWidth, 22f);
                    GUI.Label(elmRect, node.name, EditorStyles.helpBox);
                    var closeButtonRect = new Rect(elmRect.xMax - closeButtonWidth + 2f, elmRect.y + 3f, closeButtonWidth - 4f, 18f);
                    if (GUI.Button(closeButtonRect, "X") && Event.current.button == 0) {
                        selectedNodes.Remove(node);
                        selectedNodesNeedRecache = true;
                    }
                    currentX += width + closeButtonWidth;
                }
            }
            GUI.EndScrollView();
        }

        private void DrawTooltip(Rect p_rect) {
            _contentHeight += 60;
            if (_hoverNode == null || _hoverNode.content == null || string.IsNullOrWhiteSpace(_hoverNode.content.tooltip))
                return;

            GUIStyle style = new GUIStyle();
            style.fontSize = 9;
            style.wordWrap = true;
            style.normal.textColor = Color.white;
            GUI.Label(p_rect, _hoverNode.content.tooltip, style);
        }

        private void HandleResize(Rect p_rect) {
            if (!isResizable) return;

            float windowWidth = editorWindow != null ? editorWindow.position.width : width;
            float windowHeight = editorWindow != null ? editorWindow.position.height : height;

            var resizeRect = new Rect(windowWidth - 20f, windowHeight - 20f, 20f, 20f);
            EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeUpLeft);

            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition)) {
                isResizing = true;
                Event.current.Use();
            }

            if (isResizing) {
                if (Event.current.type == EventType.MouseDrag) {
                    width = (int)Mathf.Clamp(Event.current.mousePosition.x, minSize.x, maxSize.x);
                    height = (int)Mathf.Clamp(Event.current.mousePosition.y, minSize.y, maxSize.y);
                    maxHeight = height;
                    resizeToContent = false;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseUp) {
                    isResizing = false;
                    Event.current.Use();
                }
            }

            // Always enforce min/max on editorWindow so PopupWindow host cannot clamp us
            if (editorWindow != null) {
                editorWindow.minSize = minSize;
                editorWindow.maxSize = maxSize;
            }

            if (Event.current.type == EventType.Repaint) {
                var handleStyle = GUI.skin.GetStyle("WindowBottomResize");
                if (handleStyle != null) {
                    handleStyle.Draw(resizeRect, false, false, false, false);
                }
            }
        }

        private void DrawMenuItems(Rect p_rect) {
            GUILayout.BeginArea(p_rect);
            if (_useScroll) {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar);
            }

            GUILayout.BeginVertical();

            // Always use DrawNodeSearch if a folder filter is active OR if we're in "Add New Klip" mode
            if (!string.IsNullOrEmpty(_folderFilter) || (_title != null && _title.Contains("Add New Klip"))) {
                DrawNodeSearch(p_rect);
            }
            else {
                if (string.IsNullOrEmpty(_search) && _starredNodes != null && _starredNodes.Count > 0) {
                    DrawStaredNodes(p_rect);
                }
                if (string.IsNullOrWhiteSpace(_search) || _search.Length < 2) {
                    DrawNodeTree(p_rect);
                }
                else {
                    DrawNodeSearch(p_rect);
                }
            }

            GUILayout.EndVertical();
            if (_useScroll) {
                EditorGUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private void DrawStaredNodes(Rect p_rect) {

            string lastPath = "";
            for (int i = 0; i < _starredNodes.Count; i++) {
                string nodePath = _starredNodes[i].parent.GetPath();
                if (nodePath != lastPath) {
                    _contentHeight += 21;
                    GUILayout.Label(nodePath, LabelWhite);
                    lastPath = nodePath;
                }

                _contentHeight += 21;
                DetermineNodeBackgroundColor(_starredNodes[i]);
                GUIStyle style = new GUIStyle();
                style.normal.background = EditorGUIUtility.whiteTexture;
                GUILayout.BeginHorizontal(style);

                if (showOnStatus) {
                    style = new GUIStyle("box");
                    style.normal.background = Texture2D.whiteTexture;
                    GUI.color = _starredNodes[i].on ? new Color(0, .6f, .8f) : new Color(.2f, .2f, .2f);
                    //GUILayout.Box("", style, GUILayout.Width(14), GUILayout.Height(14));
                }

                GUI.color = _hoverNode == _starredNodes[i] ? Color.white : Color.white;
                GUILayout.Label("⋆ " + _starredNodes[i].name, LabelWhite);

                GUILayout.EndHorizontal();

                var nodeRect = GUILayoutUtility.GetLastRect();
                if (Event.current.isMouse) {
                    if (nodeRect.Contains(Event.current.mousePosition)) {
                        hoveredIndex = i;
                        if (Event.current.type == EventType.MouseDown) {
                            if (Event.current.button == 0) {
                                if (_starredNodes[i].Nodes.Count > 0) {
                                    _currentNode = _starredNodes[i];
                                    _repaint = true;
                                }
                                else {
                                    if (onSearchTermChanged != null) {
                                        onSearchTermChanged.Invoke(_search);
                                    }
                                    if (Event.current.clickCount == 2) {
                                        doubleClicked = true;
                                    }
                                    else {
                                        SelectNode(_starredNodes[i]);
                                    }
                                }

                                break;
                            }
                            else if (Event.current.button == 1) {
                                HandleRightClick(_starredNodes[i]);
                            }
                        }

                        if (_hoverNode != _starredNodes[i]) {
                            _hoverNode = _starredNodes[i];
                            _repaint = true;
                        }
                    }
                    else if (_hoverNode == _starredNodes[i]) {
                        _hoverNode = null;
                        _repaint = true;
                    }
                }
            }

            if (_starredNodes.Count == 0) {
                GUILayout.Label("No result found for specified search.");
            }
        }

        private void DrawNodeSearch(Rect p_rect) {
            bool searchDirty = _cachedSearchResults == null
                || _cachedSearchTerm != (_search ?? "")
                || _cachedFolderFilter != (_folderFilter ?? "");

            if (searchDirty) {
                _cachedSearchTerm = _search ?? "";
                _cachedFolderFilter = _folderFilter ?? "";

                List<MenuItemNode> results = _rootNode.Search(_cachedSearchTerm);

                if (!string.IsNullOrEmpty(_cachedFolderFilter)) {
                    string lowerFilter = _cachedFolderFilter.ToLower();
                    results = results.Where(n => {
                        if (n.Nodes.Count > 0) return false; // Only show files in result list
                        string path = n.GetPath().ToLower();
                        return path.Contains(lowerFilter);
                    }).ToList();
                }
                else {
                    // "All" mode: Show everything from all folders (excluding the category folders themselves)
                    results = results.Where(n => n.Nodes.Count == 0).ToList();
                }

                results.Sort((n1, n2) => {
                    string p1 = n1.parent.GetPath();
                    string p2 = n2.parent.GetPath();
                    if (p1 == p2)
                        return n1.name.CompareTo(n2.name);

                    return p1.CompareTo(p2);
                });

                _cachedSearchResults = results;
            }

            List<MenuItemNode> search = _cachedSearchResults;
            string lastPath = "";
            for (int i = 0; i < search.Count; i++) {
                string nodePath = search[i].parent.GetPath();
                if (nodePath != lastPath) {
                    _contentHeight += 21;
                    GUILayout.Label(nodePath, LabelWhite);
                    lastPath = nodePath;
                }

                _contentHeight += 21;
                DetermineNodeBackgroundColor(search[i]);
                GUIStyle style = new GUIStyle();
                style.normal.background = EditorGUIUtility.whiteTexture;
                GUILayout.BeginHorizontal(style);

                if (showOnStatus) {
                    style = new GUIStyle("box");
                    style.normal.background = Texture2D.whiteTexture;
                    GUI.color = search[i].on ? new Color(0, .6f, .8f) : new Color(.2f, .2f, .2f);
                    //GUILayout.Box("", style, GUILayout.Width(14), GUILayout.Height(14));
                }

                GUI.color = _hoverNode == search[i] ? Color.white : Color.white;
                GUILayout.Label(search[i].name, LabelWhite);

                GUILayout.EndHorizontal();

                var nodeRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown) {
                    if (nodeRect.Contains(Event.current.mousePosition)) {
                        hoveredIndex = i;
                        if (Event.current.button == 0) {
                            if (search[i].Nodes.Count > 0) {
                                _currentNode = search[i];
                                _repaint = true;
                            }
                            else {
                                if (onSearchTermChanged != null) {
                                    onSearchTermChanged.Invoke(_search);
                                }
                                if (Event.current.clickCount == 2) {
                                    doubleClicked = true;
                                }
                                else {
                                    SelectNode(search[i]);
                                }
                            }
                            Event.current.Use();
                            break;
                        }
                        else if (Event.current.button == 1) {
                            HandleRightClick(search[i]);
                            Event.current.Use();
                        }
                    }
                }

                if (Event.current.type == EventType.MouseMove) {
                    if (nodeRect.Contains(Event.current.mousePosition)) {
                        hoveredIndex = i;
                        if (_hoverNode != search[i]) {
                            _hoverNode = search[i];
                            _repaint = true;
                        }
                    }
                    else if (_hoverNode == search[i]) {
                        _hoverNode = null;
                        _repaint = true;
                    }
                }
            }

            if (search.Count == 0) {
                GUILayout.Label("No result found for specified search.");
            }
        }

        private void DrawNodeTree(Rect p_rect) {
            if (_currentNode != _rootNode) {
                _contentHeight += 21;
                if (GUILayout.Button(_currentNode.GetPath(), BackStyle)) {
                    _currentNode = _currentNode.parent;
                }
            }

            int nodeIndex = 0;
            foreach (var node in _currentNode.Nodes) {
                if (node.separator) {
                    GUILayout.Space(4);
                    _contentHeight += 4;
                    continue;
                }

                if (ShouldSkipNode(node)) {
                    continue;
                }

                _contentHeight += 21;

                TryBeginColumnLayout(nodeIndex);

                DetermineNodeBackgroundColor(node, nodeIndex);
                GUIStyle style = new GUIStyle();
                style.normal.background = EditorGUIUtility.whiteTexture;
                GUILayout.BeginHorizontal(style);

                if (showOnStatus) {
                    style = new GUIStyle("box");
                    style.normal.background = Texture2D.whiteTexture;
                    GUI.color = node.on ? new Color(0, .6f, .8f, .5f) : new Color(.2f, .2f, .2f, .2f);
                    //GUILayout.Box("", style, GUILayout.Width(14), GUILayout.Height(14));
                }

                GUI.color = _hoverNode == node ? Color.white : Color.white;
                style = LabelWhite;
                style.fontStyle = node.Nodes.Count > 0 ? FontStyle.Bold : FontStyle.Normal;
                GUILayout.Label(node.name, style, GUILayout.Width((_frameWidth - 30f) / _effectiveColumnCount));

                GUILayout.EndHorizontal();
                var nodeRect = GUILayoutUtility.GetLastRect();

                TryEndColumnLayout(nodeIndex);
                nodeIndex++;

                if (Event.current.type == EventType.MouseDown) {
                    if (nodeRect.Contains(Event.current.mousePosition)) {
                        if (Event.current.button == 0) {
                            if (node.Nodes.Count > 0) {
                                _currentNode = node;
                                _repaint = true;
                            }
                            else {
                                if (onSearchTermChanged != null) {
                                    onSearchTermChanged.Invoke(_search);
                                }
                                if (Event.current.clickCount == 2) {
                                    doubleClicked = true;
                                }
                                else {
                                    SelectNode(node);
                                }
                            }
                            Event.current.Use();
                            break;
                        }
                        else if (Event.current.button == 1) {
                            HandleRightClick(node);
                            Event.current.Use();
                        }
                    }
                }

                if (Event.current.type == EventType.MouseMove) {
                    if (nodeRect.Contains(Event.current.mousePosition)) {
                        if (_hoverNode != node) {
                            _hoverNode = node;
                            _repaint = true;
                        }
                    }
                    else if (_hoverNode == node) {
                        _hoverNode = null;
                        _repaint = true;
                    }
                }

                if (node.Nodes.Count > 0) {
                    Rect lastRect = GUILayoutUtility.GetLastRect();
                    GUI.Label(new Rect(lastRect.x + lastRect.width - 16, lastRect.y - 2, 20, 20), "+", PlusStyle);
                }
            }

            int indicesLeft = _effectiveColumnCount - (nodeIndex % _effectiveColumnCount);
            if (indicesLeft == _effectiveColumnCount) indicesLeft = 0;
            for (int i = 0; i < indicesLeft; i++) {
                GUILayout.Label(GUIContent.none, GUILayout.Width((_frameWidth - 30f) / _effectiveColumnCount));
                nodeIndex++;
            }

            TryEndColumnLayout(nodeIndex);
        }

        protected virtual bool ShouldSkipNode(MenuItemNode node) {
            return false;
        }

        private void TryBeginColumnLayout(int nodeIndex) {
            if (_effectiveColumnCount == 1) return;
            if (nodeIndex == 0 || nodeIndex % _effectiveColumnCount == 0) {
                GUILayout.BeginHorizontal();
            }
        }

        private void TryEndColumnLayout(int nodeIndex) {
            if (_effectiveColumnCount == 1) return;
            if (nodeIndex % _effectiveColumnCount == (_effectiveColumnCount - 1)) {
                GUILayout.EndHorizontal();
            }
        }

        protected virtual void DetermineNodeBackgroundColor(MenuItemNode node, int nodeIndex = 0) {
            if (selectedNodes.Contains(node)) {
                Color baseColor = new Color(0.4f, 0.4f, 0.8f);
                GUI.color = _hoverNode == node ? baseColor * 1.5f : baseColor;
            }
            else {
                if (_hoverNode == node) {
                    GUI.color = new Color(0.55f, 0.55f, 0.55f, 1f);
                }
                else {
                    if (nodeIndex % 2 == 0) {
                        GUI.color = new Color(0.3f, 0.3f, 0.3f, 1);
                    }
                    else {
                        GUI.color = new Color(0.25f, 0.25f, 0.25f, 1);
                    }
                }
            }
        }

        private void HandleRightClick(MenuItemNode node) {
            onRightClicked?.Invoke(node.userData);
        }

        void OnEditorUpdate() {
            if (_repaint) {
                _repaint = false;
                base.editorWindow.Repaint();
            }
        }

        // TODO Possible type caching? 
        internal MenuItemNode GenerateMenuItemNodeTree(GenericMenu p_menu, out float maxColumnWidth) {
            maxColumnWidth = 1f;
            MenuItemNode rootNode = new MenuItemNode();
            if (p_menu == null)
                return rootNode;

            var menuItemsField = p_menu.GetType().GetField("menuItems", BindingFlags.Instance | BindingFlags.NonPublic);
            IList menuItems;

            if (menuItemsField == null) {   //Unity 2021.2
                menuItemsField = p_menu.GetType().GetField("m_MenuItems", BindingFlags.Instance | BindingFlags.NonPublic);
                menuItems = menuItemsField.GetValue(p_menu) as IList;
            }

            else { //Older Unity Versions 
                menuItems = menuItemsField.GetValue(p_menu) as ArrayList;

            }

            var labelStyle = EditorStyles.label;

            foreach (var menuItem in menuItems) {
                var menuItemType = menuItem.GetType();
                GUIContent content = (GUIContent)menuItemType.GetField("content").GetValue(menuItem);

                float textWidth = labelStyle.CalcSize(content).x + 2f;
                if (textWidth > maxColumnWidth) maxColumnWidth = textWidth;

                bool separator = (bool)menuItemType.GetField("separator").GetValue(menuItem);
                string path = content.text;
                string[] splitPath = path.Split('/');
                MenuItemNode currentNode = rootNode;
                for (int i = 0; i < splitPath.Length; i++) {
                    currentNode = (i < splitPath.Length - 1)
                        ? currentNode.GetOrCreateNode(splitPath[i])
                        : (separator ? currentNode.CreateNode(splitPath[i]) : currentNode.GetOrCreateNode(splitPath[i]));
                }

                if (separator) {
                    currentNode.separator = true;
                }
                else {
                    currentNode.content = content;
                    currentNode.func = (GenericMenu.MenuFunction)menuItemType.GetField("func").GetValue(menuItem);
                    currentNode.func2 = (GenericMenu.MenuFunction2)menuItemType.GetField("func2").GetValue(menuItem);
                    currentNode.userData = menuItemType.GetField("userData").GetValue(menuItem);
                    currentNode.on = (bool)menuItemType.GetField("on").GetValue(menuItem);
                    if (currentNode.on) {
                        selectedNodes.Add(currentNode);
                        selectedNodesNeedRecache = true;
                    }
                }
            }

            DeepSortNodes(rootNode);

            return rootNode;
        }

        private static void DeepSortNodes(MenuItemNode _currentNode) {
            List<List<MenuItemNode>> separatedNodes = new List<List<MenuItemNode>>();

            int i = 0;
            separatedNodes.Add(new List<MenuItemNode>());
            foreach (var node in _currentNode.Nodes) {
                separatedNodes[i].Add(node);
                if (node.separator) {
                    separatedNodes.Add(new List<MenuItemNode>());
                    i++;
                }
            }

            _currentNode.Nodes.Clear();
            for (i = 0; i < separatedNodes.Count; i++) {
                separatedNodes[i].Sort((n1, n2) => {
                    if (n1.separator) return 1;
                    if (n1.Nodes.Count == 0 && n2.Nodes.Count > 0) return -1;
                    if (n1.Nodes.Count > 0 && n2.Nodes.Count == 0) return 1;
                    return n1.name.CompareTo(n2.name);
                });
                _currentNode.Nodes.AddRange(separatedNodes[i]);
            }
            foreach (var childNode in _currentNode.Nodes) {
                DeepSortNodes(childNode);
            }
        }

        private void SelectNode(MenuItemNode _selectedNode) {
            //_selectedNode.Execute();
            //base.editorWindow.Close();
            if (selectedNodes.Contains(_selectedNode)) {
                selectedNodes.Remove(_selectedNode);
            }
            else {
                selectedNodes.Add(_selectedNode);
            }
            selectedNodesNeedRecache = true;
            _repaint = true;
        }

        public override void OnOpen() {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        public override void OnClose() {
            EditorApplication.update -= OnEditorUpdate;
        }

        internal static void Show(GenericMenu menu, string v1, Vector2 mousePosition, List<string> list, string v2, object value1, object value2, int v3, bool v4, object onClickSaveTypesPreset) {
            throw new System.NotImplementedException();
        }
    }
}