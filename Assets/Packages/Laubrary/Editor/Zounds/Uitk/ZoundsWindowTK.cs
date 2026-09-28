using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the main Zounds window (T-0470): the "Alternative" box, the row of main tabs (Browser,
    /// Monitor, Routing, Dep. Map, Settings) and the selected tab's content. It works on the same project as the old
    /// window through the same shared code; nothing here loads, saves or owns the project — the old window's statics do.
    ///
    /// Its selected tab is its own (kept with the window), started from the old window's, so the two windows can show
    /// different tabs side by side; everything else (search text, filters, browser settings) is the shared state the old
    /// window uses.
    /// </summary>
    public class ZoundsWindowTK : ZuiWindow, IHasCustomMenu {

        [SerializeField] int selectedTab = -1;

        internal static readonly string[] TabNames = { "Browser", "Monitor", "Routing", "Dep. Map", "Settings" };

        VisualElement content, box;
        readonly List<ZuiToggleButton> tabButtons = new List<ZuiToggleButton>();
        internal BrowserTabTK browser;
        MonitorTabTK monitor;
        RoutingTabTK routing;
        SettingsTabTK settings;
#if ADDRESSABLES_INSTALLED
        DependencyMapTabTK depMap;
#endif
        IVisualElementScheduledItem tick, live;
        bool wasLoaded;

        public static ZoundsWindowTK OpenWindow() {
            var w = GetWindow<ZoundsWindowTK>();
            w.titleContent = new GUIContent("Zounds (UITK)");
            w.minSize = new Vector2(414f, 151f);
            w.Show();
            return w;
        }

        public void AddItemsToMenu(GenericMenu menu) {
            menu.AddItem(new GUIContent("Grid Mode (Zounds Browser)"), ZoundsProject.Instance.browserSettings.multicolumn, ZoundsWindow.ToggleColumnView);
        }

        protected override void BuildUI(VisualElement root) {
            ZS.Attach(root);
            root.AddToClassList("zs-mainwindow");
            titleContent = new GUIContent("Zounds (UITK)");
            tabButtons.Clear();
            browser = null;

            box = new VisualElement();
            box.AddToClassList("zs-box-alternative");
            box.style.flexGrow = 1;
            root.Add(box);

            wasLoaded = ZoundsProject.isJSONLoaded;
            if (!wasLoaded) {
                box.Add(ZequenceEditorWindowTK.Space(30f));
                var l = new Label("No Zounds Project Loaded");
                l.AddToClassList("zs-lbl"); l.AddToClassList("zs-bold");
                l.style.height = 18f;
                box.Add(l);
                box.Add(ProjectFileRow(() => Rebuild()));
                tick = root.schedule.Execute(() => { if (ZoundsProject.isJSONLoaded != wasLoaded) Rebuild(); }).Every(250);
                return;
            }

            if (selectedTab < 0 || selectedTab >= TabNames.Length) selectedTab = Mathf.Clamp(ZoundsWindowProperties.Instance.selectedMainTab, 0, TabNames.Length - 1);
            var tabs = new VisualElement();
            tabs.AddToClassList("zs-maintabs");
            tabs.style.flexDirection = FlexDirection.Row; tabs.style.flexShrink = 0;
            for (int i = 0; i < TabNames.Length; i++) {
                int index = i;
                var t = ZS.Toggle(TabNames[i], "", i == selectedTab, v => SelectTab(index), "MainTab", ZUICornerMask.None, -1f, 30f);
                t.style.flexGrow = 1; t.style.flexShrink = 1;
                tabs.Add(t);
                tabButtons.Add(t);
            }
            box.Add(tabs);

            content = new VisualElement();
            content.style.flexGrow = 1; content.style.flexShrink = 1;
            box.Add(content);
            ShowTab();

            tick = root.schedule.Execute(Tick).Every(200);
            live = root.schedule.Execute(Live).Every(33);
        }

        void SelectTab(int index) {
            selectedTab = index;
            for (int i = 0; i < tabButtons.Count; i++) tabButtons[i].SetValueWithoutNotify(i == selectedTab);
            ShowTab();
        }

        void ShowTab() {
            content.Clear();
            SetFill(true);
            browser = null; monitor = null; routing = null; settings = null;
#if ADDRESSABLES_INSTALLED
            depMap = null;
#endif
            switch (selectedTab) {
                case 0: browser = new BrowserTabTK(this); content.Add(browser); break;
                case 1: monitor = new MonitorTabTK(this); content.Add(monitor); break;
                case 2: routing = new RoutingTabTK(this); content.Add(routing); break;
#if ADDRESSABLES_INSTALLED
                case 3: depMap = new DependencyMapTabTK(this); content.Add(depMap); break;
#endif
                case 4: settings = new SettingsTabTK(); content.Add(settings); break;
                default:
                    var todo = new Label(TabNames[selectedTab]);
                    todo.AddToClassList("zs-lbl");
                    content.Add(todo);
                    break;
            }
        }

        void Tick() {
            if (ZoundsProject.isJSONLoaded != wasLoaded) { Rebuild(); return; }
            browser?.Tick();
            monitor?.Tick();
            routing?.Tick();
            settings?.Tick();
#if ADDRESSABLES_INSTALLED
            depMap?.Tick();
            TintDepMapTab();
#endif
        }

        void Live() { browser?.Live(); monitor?.Live(); routing?.Live(); }

        internal void Refresh() => ShowTab();

#if ADDRESSABLES_INSTALLED
        double lastBrokenCheck = -100;
        /// <summary>The old tab row tints "Dep. Map" red while any zound has a broken reference (its analysis runs every
        /// 5 s whichever tab is open); the same check here, at the same interval.</summary>
        void TintDepMapTab() {
            if (tabButtons.Count < 4 || EditorApplication.timeSinceStartup - lastBrokenCheck < 5.0) return;
            lastBrokenCheck = EditorApplication.timeSinceStartup;
            bool broken = ZoundDependencyAnalyzer.Analyze().brokenZounds.Count > 0;
            tabButtons[3].EnableInClassList("zs-maintab--broken", broken);
        }
#endif

        /// <summary>
        /// Whether the "Alternative" box reaches the window's bottom. In the old window it does only when the tab's content
        /// expands (a scroll view); otherwise the box ends under the content and the editor's own background shows below.
        /// </summary>
        internal void SetFill(bool fill) {
            if (box != null) box.style.flexGrow = fill ? 1 : 0;
            if (content != null) content.style.flexGrow = fill ? 1 : 0;
        }

        protected override void OnDisable() {
            tick?.Pause(); live?.Pause();
            base.OnDisable();
        }

        // ─────────────────────────── the project-file row ───────────────────────────

        /// <summary>
        /// ZoundsWindow.DrawJSONProjectField: "Project JSON" object field (label 80), Create New (85), Load (60), Auto-Save
        /// (label 65, 82 wide) and Save (60) — every action is the old window's own shared one.
        /// </summary>
        internal static VisualElement ProjectFileRow(Action changed) {
            var row = new VisualElement();
            row.AddToClassList("zs-projectrow");
            row.style.flexDirection = FlexDirection.Row; row.style.flexShrink = 0;
            var field = new ObjectField("Project JSON") { objectType = typeof(TextAsset), allowSceneObjects = false, value = ZoundsWindow.CurrentProjectJSON };
            field.AddToClassList("zs-imgui-field"); field.AddToClassList("zs-label-80");
            field.style.flexGrow = 1; field.style.flexShrink = 1;
            field.RegisterValueChangedCallback(e => { ZoundsWindow.AssignProjectJSON(e.newValue as TextAsset); changed?.Invoke(); });
            row.Add(field);
            var create = new Button(() => { ZoundsWindow.CreateNewProject(); changed?.Invoke(); }) { text = "Create New" };
            create.AddToClassList("zs-imgui-button"); create.style.width = 85f;
            row.Add(create);
            var load = new Button(() => { ZoundsWindow.LoadProject(); changed?.Invoke(); }) { text = "Load" };
            load.AddToClassList("zs-imgui-button"); load.style.width = 60f;
            row.Add(load);
            var auto = new Toggle("Auto-Save") { value = ZoundsWindowProperties.Instance.autoSave };
            auto.AddToClassList("zs-imgui-field"); auto.AddToClassList("zs-label-65");
            auto.style.width = 82f;
            auto.RegisterValueChangedCallback(e => ZoundsWindow.SetAutoSave(e.newValue));
            row.Add(auto);
            var save = new Button(ZoundsWindow.SaveToJSON) { text = "Save" };
            save.AddToClassList("zs-imgui-button"); save.style.width = 60f;
            row.Add(save);
            void Sync() {
                bool has = ZoundsWindow.CurrentProjectJSON != null;
                load.SetEnabled(!ReferenceEquals(ZoundsWindow.CurrentProjectJSON, null));
                auto.SetEnabled(has);
                auto.SetValueWithoutNotify(ZoundsWindowProperties.Instance.autoSave);
                save.SetEnabled(has && ZoundsWindow.zoundsProjectDirty);
                if (field.value != ZoundsWindow.CurrentProjectJSON) field.SetValueWithoutNotify(ZoundsWindow.CurrentProjectJSON);
            }
            Sync();
            row.schedule.Execute(Sync).Every(250);
            return row;
        }
    }
}
