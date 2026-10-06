using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {
    
    public class TabContent {
        internal EditorWindow previewOwner;
        public virtual string name { get; set; }
        public virtual string tooltip { get; set; }
        public virtual bool isVisible => true;
        public virtual Color headerColor => Color.white;
        public virtual void Update() { }
        public virtual void OnTabOpened() { }
        public virtual void OnGUI(SerializedObject serializedObject, Rect contentRect) { }
    }

    public class TabViewIMGUI {

        private List<TabContent> tabElements;
        private List<TabContent> visibleTabs;
        private GUIContent[] tabNameContents;

        public int tabCount => tabElements.Count;

        public TTabContent GetTab<TTabContent>(int index) where TTabContent : TabContent {
            return tabElements.FirstOrDefault(t => t is TTabContent) as TTabContent;
        }

        public TabViewIMGUI(params TabContent[] tabElements) {
            this.tabElements = new List<TabContent>(tabElements);
            UpdateVisibleTabs();
        }

        public void Update() {
            foreach (var tab in tabElements) {
                tab.Update();
            }
        }

        private void UpdateVisibleTabs() {
            visibleTabs = tabElements.Where(t => t.isVisible).ToList();
            tabNameContents = visibleTabs.Select(t => new GUIContent(t.name, t.tooltip)).ToArray();
        }

        public int DrawLayout(int tabIndex, SerializedObject serializedObject, Rect viewportRect) {
      
            UpdateVisibleTabs();

            if (visibleTabs.Count == 0) return 0;

            // Ensure tabIndex is within bounds of visible tabs
            if (tabIndex >= visibleTabs.Count) tabIndex = 0;

            if (visibleTabs.Count > 1) {
                var prevColor = GUI.color;
                int selectedTab = tabIndex;

                GUILayout.BeginHorizontal();
                for (int i = 0; i < visibleTabs.Count; i++) {
                    GUI.color = visibleTabs[i].headerColor;
                    bool isActive = (tabIndex == i);
                    bool newActive = ZUI.Toggle(isActive, tabNameContents[i], ZUI.Style.MainTab,
                        GUILayout.Height(30f), GUILayout.ExpandWidth(true));
                    if (newActive && !isActive) {
                        selectedTab = i;
                    }
                }
                GUILayout.EndHorizontal();
                GUI.color = prevColor;

                if (tabIndex != selectedTab) {
                    visibleTabs[selectedTab].OnTabOpened();
                }
                visibleTabs[selectedTab].OnGUI(serializedObject, viewportRect);
                return selectedTab;
            }
            else {
                visibleTabs[0].OnGUI(serializedObject, viewportRect);
                return 0;
            }
        }

    }

}