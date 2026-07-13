using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class TagMenuPopup : GenericMenuPopup {

        public static bool showKeysOnly {
            get => EditorPrefs.GetBool("ShowKeysOnly", false);
            set => EditorPrefs.SetBool("ShowKeysOnly", value);
        }

        public TagMenuPopup(GenericMenu p_menu, string p_title, List<string> p_starredPaths, int p_columnCount = 3, bool p_invokeNoneSelected = false) : base(p_menu, p_title, p_starredPaths, p_columnCount, p_invokeNoneSelected) {
        }

        public static GenericMenuPopup ShowTagMenu(GenericMenu p_menu, string p_title, Vector2 p_position, List<string> starredPaths,
            string _searchTerm = "", System.Action<string> _onSearchTermChanged = null, 
            System.Action<object> _onRightClicked = null, int _columnCount = 3, bool _invokeNoneSelected = false,
            List<ZoundsEditorPresets.NameListPreset> presetList = null) {
            
            var popup = new TagMenuPopup(p_menu, p_title, starredPaths, _columnCount, _invokeNoneSelected);
            popup.onSearchTermChanged = _onSearchTermChanged;
            popup._search = _searchTerm;
            popup.resizeToContent = false;
            popup.onRightClicked = _onRightClicked;
            popup.presetList = presetList;
            popup.lastSelectedPresetName = null;
            popup.isResizable = false;

            Vector2 screenPos = GUIUtility.GUIToScreenPoint(p_position);
            GenericMenuEditorWindow.Show(p_menu, p_title, screenPos, starredPaths,
                _searchTerm, _onSearchTermChanged, _onRightClicked, _columnCount, _invokeNoneSelected,
                presetList, null);

            return popup;
        }

        protected override float OnDrawCustomToggles(Rect searchRect) {
            float width = 83f;
            var keysOnlyRect = new Rect(searchRect.xMax - width, searchRect.y, width, searchRect.height);
            EditorGUI.BeginChangeCheck();
            var showKeysOnlyTemp = EditorGUI.ToggleLeft(keysOnlyRect, "Keys Only", showKeysOnly);
            if (EditorGUI.EndChangeCheck()) {
                showKeysOnly = showKeysOnlyTemp;
            }
            return width;
        }

        protected override void DetermineNodeBackgroundColor(MenuItemNode node, int nodeIndex = 0) {
            base.DetermineNodeBackgroundColor(node, nodeIndex);
            if (!showKeysOnly && !node.name.Contains(':')) {
                if (selectedNodes.Contains(node)) {

                }
                else {
                    GUI.color *= Color.cyan;
                }
            }
        }

        protected override bool ShouldSkipNode(MenuItemNode node) {
            return showKeysOnly && node.name.Contains(':');
        }

    }

}
