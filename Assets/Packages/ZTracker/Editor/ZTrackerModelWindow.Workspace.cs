using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // Window-owned view state is serialized by Unity across assembly reloads. No authored
        // song/instrument is dirtied, and a second window keeps its own position and folds.
        [Serializable] sealed class WorkspaceScroll { public string key; public Vector2 offset; }
        [Serializable] sealed class WorkspaceFold { public string key; public bool open; }
        [Serializable] sealed class WorkspacePage
        {
            public string key;
            public List<WorkspaceScroll> scrolls = new List<WorkspaceScroll>();
            public List<WorkspaceFold> folds = new List<WorkspaceFold>();
        }
        [SerializeField] List<WorkspacePage> workspacePages = new List<WorkspacePage>();
        string displayedWorkspace;
        bool workspaceRestoring;
        int workspaceGeneration;

        string WorkspaceKey() => (Data?.id ?? "empty") + "/" + pane + "/" +
            (pane == 3 ? (Instrument?.id ?? "none") + "/" + instrumentTab :
             pane == 1 ? (SelectedTrack?.id ?? "none") + "/" + mixerTab :
             pane == 2 ? (SelectedTrack?.id ?? "none") : "");

        static string ScrollKey(ScrollView scroll, int index) =>
            string.IsNullOrEmpty(scroll.name) ? "scroll-" + index : scroll.name;

        WorkspacePage WorkspacePageFor(string key)
        {
            var page = workspacePages.Find(p => p.key == key);
            if (page != null) return page;
            page = new WorkspacePage { key = key }; workspacePages.Add(page); return page;
        }

        void CaptureWorkspace()
        {
            if (displayedWorkspace == null || workspaceRestoring || rootVisualElement.childCount == 0) return;
            var page = WorkspacePageFor(displayedWorkspace);
            page.scrolls.Clear();
            var scrolls = rootVisualElement.Query<ScrollView>().ToList();
            for (int i = 0; i < scrolls.Count; i++)
                page.scrolls.Add(new WorkspaceScroll { key = ScrollKey(scrolls[i], i), offset = scrolls[i].scrollOffset });
            CaptureWorkspaceFolds(page);
        }

        void CaptureWorkspaceFolds(WorkspacePage page)
        {
            var folds = new Dictionary<string, bool>();
            foreach (var box in rootVisualElement.Query<ZuiBox>().ToList()) box.CaptureView(folds);
            foreach (var value in rootVisualElement.Query<ZuiValueControl>().ToList())
                if (!string.IsNullOrEmpty(value.name)) folds["value/" + value.name] = value.IsExpanded;
            page.folds = folds.Select(p => new WorkspaceFold { key = p.Key, open = p.Value }).ToList();
        }

        void RestoreWorkspace()
        {
            displayedWorkspace = WorkspaceKey();
            var page = WorkspacePageFor(displayedWorkspace);
            var folds = page.folds.ToDictionary(p => p.key, p => p.open);
            foreach (var box in rootVisualElement.Query<ZuiBox>().ToList())
            {
                box.ApplyView(folds);
                box.ViewChanged += () => CaptureWorkspaceFolds(page);
            }
            foreach (var value in rootVisualElement.Query<ZuiValueControl>().ToList())
            {
                if (!string.IsNullOrEmpty(value.name) && folds.TryGetValue("value/" + value.name, out bool expanded)) value.IsExpanded = expanded;
                value.ExpansionChanged += () => CaptureWorkspaceFolds(page);
            }
            int generation = ++workspaceGeneration;
            workspaceRestoring = true;
            // Wait for both the retained tree and responsive columns to have real bounds.
            // This also runs after the base window's positional scroll restoration.
            rootVisualElement.schedule.Execute(() =>
            {
                if (generation != workspaceGeneration) return;
                var scrolls = rootVisualElement.Query<ScrollView>().ToList();
                for (int i = 0; i < scrolls.Count; i++)
                {
                    var saved = page.scrolls.Find(p => p.key == ScrollKey(scrolls[i], i));
                    if (saved != null) scrolls[i].scrollOffset = saved.offset;
                }
                workspaceRestoring = false;
            }).StartingIn(80);
        }
    }
}
