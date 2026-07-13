#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using UnityEngine.AddressableAssets;

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Editor tab that visualizes the full dependency graph of Zounds and AudioClips.
    /// Sections: Broken Zounds, Orphaned Files, Dependency Browser, Build AudioClips.
    /// </summary>
    public class DependencyMapTab : TabContent {

        private enum Section {
            DependencyBrowser,
            BrokenZounds,
            Orphans,
            BuildStatus
        }

        private const float RefreshIntervalSeconds = 5.0f;

        public override string name { get; set; } = "Dep. Map";
        public override string tooltip { get; set; } = "Dependency Map: relationships, orphans, broken refs, and build clips.";
        public override Color headerColor => (analyzer != null && analyzer.brokenZounds.Count > 0) ? new Color(1f, 0.4f, 0.4f) : Color.white;

        private ZoundDependencyAnalyzer analyzer;
        private ZoundsBuildReport buildReport;
        private double lastAnalysisTime;
        private bool needsRefresh = true;

        private Section activeSection = Section.DependencyBrowser;
        private Vector2 scrollPos;
        private string searchFilter = "";

        // Foldout state keyed by Zound id or clip path hash
        private HashSet<int> expandedZoundIds = new HashSet<int>();
        private bool _orphansExpanded = true;
        private bool _unusedSourcesExpanded = true;
        private bool _unusedLibraryExpanded = false;


        private GUIStyle s_foldoutRich;
        private GUIStyle s_miniLabelWrap;

        private GUIStyle FoldoutRich {
            get {
                if (s_foldoutRich == null) {
                    s_foldoutRich = new GUIStyle(EditorStyles.foldout) { richText = true };
                }
                return s_foldoutRich;
            }
        }

        private GUIStyle MiniLabelWrap {
            get {
                if (s_miniLabelWrap == null) {
                    s_miniLabelWrap = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                }
                return s_miniLabelWrap;
            }
        }
        private GUIStyle s_labelRich;
        private GUIStyle LabelRich {
            get {
                if (s_labelRich == null) {
                    s_labelRich = new GUIStyle(EditorStyles.label) { richText = true };
                }
                return s_labelRich;
            }
        }


        // ── Tab lifecycle ───────────────────────────────────────────────

        public override void OnTabOpened() {
            needsRefresh = true;
        }

        public override void Update() {
            if (needsRefresh || EditorApplication.timeSinceStartup - lastAnalysisTime > RefreshIntervalSeconds) {
                RefreshAnalysis();
            }
        }

        /// <summary>
        /// Forces an immediate re-analysis. Call from outside when project data changes.
        /// </summary>
        public void RequestRefresh() {
            needsRefresh = true;
        }

        private void RefreshAnalysis() {
            analyzer = ZoundDependencyAnalyzer.Analyze();
            lastAnalysisTime = EditorApplication.timeSinceStartup;
            needsRefresh = false;
            buildReport = null; // invalidate — regenerated on next Status tab view
        }

        // ── Main draw ───────────────────────────────────────────────────

        public override void OnGUI(SerializedObject serializedObject, Rect contentRect) {
            if (analyzer == null) RefreshAnalysis();

            using (ZUI.Box())
            {
            GUILayout.Space(4f);

            // Section toolbar
            GUILayout.BeginHorizontal();
            {
                DrawSectionButton(Section.DependencyBrowser, "Dependencies");
                DrawSectionButton(Section.BrokenZounds, $"Broken ({analyzer.brokenZounds.Count})");
                DrawSectionButton(Section.Orphans, $"Orphans ({analyzer.orphanClips.Count})");
                DrawSectionButton(Section.BuildStatus, $"Build ({analyzer.buildClips.Count})");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60f))) {
                    RefreshAnalysis();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            scrollPos = GUILayout.BeginScrollView(scrollPos);
            {
                switch (activeSection) {
                    case Section.DependencyBrowser:
                        DrawDependencyBrowser();
                        break;
                    case Section.BrokenZounds:
                        DrawBrokenZounds();
                        break;
                    case Section.Orphans:
                        DrawOrphans();
                        break;
                    case Section.BuildStatus:
                        DrawBuildStatus();
                        break;
                }
            }
            GUILayout.EndScrollView();
            } // end ZUI.Box
        }

        private void DrawSectionButton(Section section, string label) {
            bool isActive = activeSection == section;
            var prevColor = GUI.backgroundColor;
            
            if (isActive) GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
            else if (section == Section.BrokenZounds && analyzer.brokenZounds.Count > 0) GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            else if (section == Section.BuildStatus && buildReport != null && buildReport.hasIssues) GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);

            if (GUILayout.Toggle(isActive, label, EditorStyles.toolbarButton, GUILayout.Height(20f))) {
                if (!isActive) {
                    activeSection = section;
                    scrollPos = Vector2.zero;
                }
            }
            GUI.backgroundColor = prevColor;
        }

        // ── Section: Dependency Browser ─────────────────────────────────

        private void DrawDependencyBrowser() {
            GUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Search:", GUILayout.Width(50f));
                searchFilter = EditorGUILayout.TextField(searchFilter);
                if (GUILayout.Button("X", GUILayout.Width(20f))) {
                    searchFilter = "";
                    GUI.FocusControl(null);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            string filterLower = searchFilter.ToLowerInvariant();

            var sortedNodes = analyzer.zoundNodes.Values
                .Where(n => !(n.zound is ClipZound))
                .Where(n => n.zound.parentId == 0) // top-level only
                .OrderBy(n => n.zound.name)
                .ToList();

            if (!string.IsNullOrEmpty(filterLower)) {
                sortedNodes = sortedNodes
                    .Where(n => n.zound.name.ToLowerInvariant().Contains(filterLower))
                    .ToList();
            }

            if (sortedNodes.Count == 0) {
                EditorGUILayout.LabelField("No Zounds match the filter.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            foreach (var node in sortedNodes) {
                DrawZoundNode(node);
            }
        }

        private void DrawZoundNode(ZoundDependencyAnalyzer.ZoundNode node) {
            var z = node.zound;
            bool expanded = expandedZoundIds.Contains(z.id);

            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.BeginHorizontal();
                {
                    string typeLabel = GetTypeLabel(z);
                    string color = node.isBroken ? "#FF6666" : ZoundNameColor(z);
                    string clipSuffix = "";
                    if (z is Klip foldKlip && !foldKlip.HasActiveEdits() && foldKlip.audioClipRef != null && !string.IsNullOrEmpty(foldKlip.audioClipRef.AssetGUID)) {
                        string clipPath = AssetDatabase.GUIDToAssetPath(foldKlip.audioClipRef.AssetGUID);
                        if (!string.IsNullOrEmpty(clipPath))
                            clipSuffix = $" <color=#AAAAAA>({System.IO.Path.GetFileName(clipPath)})</color>";
                    }
                    string foldoutLabel = $"<color={color}>[{typeLabel}]</color> <b>{GetCleanZoundName(z)}</b>{clipSuffix}";

                    bool newExpanded = EditorGUILayout.Foldout(expanded, foldoutLabel, true, FoldoutRich);
                    if (newExpanded != expanded) {
                        if (newExpanded) expandedZoundIds.Add(z.id);
                        else expandedZoundIds.Remove(z.id);
                    }

                    if (GUILayout.Button("Play", EditorStyles.miniButton, GUILayout.Width(35f))) {
                        ZoundEngine.PlayZound(z);
                    }
                }
                GUILayout.EndHorizontal();

                if (expanded) {
                    // Broken reason
                    if (node.isBroken) {
                        var prevColor = GUI.color;
                        GUI.color = new Color(1f, 0.4f, 0.4f);
                        EditorGUILayout.LabelField(node.brokenReason, MiniLabelWrap);
                        GUI.color = prevColor;
                    }

                    // ── Dependencies (recursive tree: what does this Zound need?) ──
                    EditorGUILayout.LabelField("Dependencies:", EditorStyles.miniBoldLabel);
                    DrawDependencyTree(z, analyzer, 0);

                    // ── Dependees (what public Zounds use this one?) ──
                    var dependees = analyzer.GetTransitiveDependents(z);
                    // Filter to public Zounds only (parentId == 0, not ClipZound)
                    var publicDependees = dependees
                        .Where(d => d.parentId == 0 && !(d is ClipZound))
                        .OrderBy(d => d.name)
                        .ToList();

                    if (publicDependees.Count > 0) {
                        GUILayout.Space(4f);
                        EditorGUILayout.LabelField("Used by:", EditorStyles.miniBoldLabel);
                        foreach (var dep in publicDependees) {
                            GUILayout.BeginHorizontal();
                            {
                                PlayableZound(dep, LabelRich, true, "  ");
                            }
                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Draws the full dependency tree for a Zound, recursing into children.
        /// Shows Zequence children (local and shared), Klip clip info, all playable.
        /// </summary>
        private void DrawDependencyTree(Zound z, ZoundDependencyAnalyzer analyzerSnapshot, int depth) {
            if (depth > 10) return;

            if (z is Klip klip) {
                // If no edits, clip info is already part of the inline label — only show for edits
                if (klip.HasActiveEdits()) {
                    EditorGUI.indentLevel++;
                    DrawKlipClipInfo(klip);
                    EditorGUI.indentLevel--;
                }
            }
            else if (z is CompositeZound composite) {
                EditorGUI.indentLevel++;
                if (z is Zequence zeq && zeq.renderedClipRef != null && !string.IsNullOrEmpty(zeq.renderedClipRef.AssetGUID)) {
                    DrawClipRefLine("rendered", zeq.renderedClipPath, zeq.renderedClipRef, false);
                }

                foreach (var entry in composite.zoundEntries) {
                    if (composite.TryGetEntryZound(entry, out var child)) {
                        if (child is ClipZound) continue;

                        bool childBroken = analyzerSnapshot.zoundNodes.TryGetValue(child.id, out var childNode) && childNode.isBroken;
                        if (childBroken) GUI.color = new Color(1f, 0.5f, 0.5f);

                        // Klips with no edits: single inline row
                        if (child is Klip childKlip && TryDrawKlipInline(childKlip)) {
                            if (childBroken) GUI.color = Color.white;
                            continue;
                        }

                        // Zequences and Klips with edits: name row + recurse
                        PlayableZound(child, LabelRich, true);
                        if (childBroken) GUI.color = Color.white;

                        DrawDependencyTree(child, analyzerSnapshot, depth + 1);
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        // ── Section: Broken Zounds ──────────────────────────────────────

        private void DrawBrokenZounds() {
            if (analyzer.brokenGroups.Count == 0) {
                GUILayout.Space(20f);
                EditorGUILayout.LabelField("No broken references detected.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.LabelField($"{analyzer.brokenGroups.Count} missing file(s) identified:", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            foreach (var kvp in analyzer.brokenGroups) {
                var group = kvp.Value;
                GUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    // 1. Missing File Header
                    GUILayout.BeginHorizontal();
                    {
                        GUI.color = new Color(1f, 0.4f, 0.4f);
                        string fileName = System.IO.Path.GetFileName(group.missingKey);
                        if (string.IsNullOrEmpty(fileName)) fileName = group.missingKey;
                        EditorGUILayout.LabelField($"MISSING: {fileName}", EditorStyles.boldLabel);
                        GUI.color = Color.white;
                    }
                    GUILayout.EndHorizontal();

                    EditorGUILayout.LabelField(group.missingKey, EditorStyles.miniLabel);
                    GUILayout.Space(4f);

                    // 2. Affected Zounds list
                    EditorGUILayout.LabelField("Referenced by:", EditorStyles.miniBoldLabel);
                    foreach (var rootEntry in group.affectedHierarchy) {
                        var rootName = rootEntry.Key;
                        var children = rootEntry.Value;

                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        {
                            EditorGUILayout.LabelField($"<b>{rootName}</b>", LabelRich);
                            
                            foreach (var childEntry in children) {
                                var childName = childEntry.Key;
                                var slots = childEntry.Value;
                                
                                string display = string.IsNullOrEmpty(childName) ? "" : $"{childName}";
                                foreach (var slot in slots) {
                                    if (!string.IsNullOrEmpty(display)) display += " / ";
                                    display += slot;
                                }

                                EditorGUILayout.LabelField($"  <color=#FF4444>•</color> {display}", LabelRich);
                            }
                        }
                        GUILayout.EndVertical();
                    }

                    GUILayout.Space(8f);

                    // 3. Group Fix UI
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        GUILayout.BeginHorizontal();
                        {
                            EditorGUI.BeginChangeCheck();
                            var selectedClip = EditorGUILayout.ObjectField("Replace with:", group.stagedFix, typeof(AudioClip), false) as AudioClip;
                            if (EditorGUI.EndChangeCheck()) {
                                group.stagedFix = selectedClip;
                            }

                            if (group.stagedFix != null) {
                                var originalBg = GUI.backgroundColor;
                                GUI.backgroundColor = Color.green;
                                if (GUILayout.Button("FIX ALL", GUILayout.Width(80f), GUILayout.Height(18f))) {
                                    ApplyGroupFix(group);
                                }
                                GUI.backgroundColor = originalBg;

                                if (GUILayout.Button("X", GUILayout.Width(20f))) {
                                    group.stagedFix = null;
                                }
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                }
                GUILayout.EndVertical();
                GUILayout.Space(4f);
            }
        }

        private void ApplyGroupFix(ZoundDependencyAnalyzer.BrokenGroup group) {
            if (group.stagedFix == null) return;

            string newPath = AssetDatabase.GetAssetPath(group.stagedFix);
            string newGuid = AssetDatabase.AssetPathToGUID(newPath);

            ZoundsWindow.ModifyAndSaveZoundsProject($"fix {group.links.Count} broken references", () => {
                var clipRef = new UnityEngine.AddressableAssets.AssetReference(newGuid);
                
                // Get all affected Zounds from the links
                var library = ZoundsProject.Instance.zoundLibrary;
                foreach (var link in group.links) {
                    var z = library.FindZound(zound => zound.id == link.zoundId);
                    if (z == null) continue;

                    if (z is Klip klip) {
                        if (MatchesMissingKey(klip.audioClipRef, klip.audioClipPath, group.missingKey)) {
                            klip.audioClipRef = clipRef;
                            klip.audioClipPath = newPath;
                            klip.needsRender = true;
                        }
                        if (MatchesMissingKey(klip.renderedClipRef, klip.renderedClipPath, group.missingKey)) {
                            klip.renderedClipRef = clipRef;
                            klip.renderedClipPath = newPath;
                        }

                        if (Application.isPlaying && ZoundEngine.IsInitialized()) {
                            ZoundDictionary.ValidateZoundRuntime(klip);
                        }
                    } else if (z is Zequence zeq) {
                        if (MatchesMissingKey(zeq.renderedClipRef, zeq.renderedClipPath, group.missingKey)) {
                            zeq.renderedClipRef = clipRef;
                            zeq.renderedClipPath = newPath;
                        }
                    }
                }
            }, true);

            #if ADDRESSABLES_INSTALLED
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null) {
                var entry = settings.CreateOrMoveEntry(newGuid, settings.DefaultGroup);
                if (entry != null) entry.address = newPath;
            }
            #endif

            RequestRefresh();
        }

        private bool MatchesMissingKey(AssetReference clipRef, string path, string missingKey) {
            if (clipRef != null && clipRef.AssetGUID == missingKey) return true;
            if (path == missingKey) return true;
            return false;
        }

        // ── Section: Orphans ────────────────────────────────────────────

        private void DrawOrphans() {
            // Orphans Section (Work/Rendered Files)
            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.BeginHorizontal();
                _orphansExpanded = EditorGUILayout.Foldout(_orphansExpanded, $"Orphans ({analyzer.orphanClips.Count})", true, EditorStyles.boldLabel);
                if (_orphansExpanded && analyzer.orphanClips.Count > 0) {
                    if (GUILayout.Button("Delete All Orphans", GUILayout.Width(130f))) {
                        var paths = analyzer.orphanClips.Select(c => c.assetPath).ToList();
                        bool confirmed = EditorUtility.DisplayDialog(
                            "Delete Orphans",
                            $"Delete {paths.Count} orphaned file(s)?\n\n{string.Join("\n", paths.Take(15))}" +
                            (paths.Count > 15 ? $"\n...and {paths.Count - 15} more" : ""),
                            "Delete", "Cancel");
                        if (confirmed) {
                            foreach (var p in paths) AssetDatabase.DeleteAsset(p);
                            RefreshAnalysis();
                        }
                    }
                }
                GUILayout.EndHorizontal();

                if (_orphansExpanded) {
                    if (analyzer.orphanClips.Count > 0) {
                        GUILayout.Space(4f);
                        foreach (var cn in analyzer.orphanClips) {
                            DrawCleanupEntry(cn);
                        }
                    } else {
                        EditorGUILayout.LabelField("All Work/Rendered files are in use", EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(12f);

            // Unused Source Section
            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                _unusedSourcesExpanded = EditorGUILayout.Foldout(_unusedSourcesExpanded, $"Unused in Source folder ({analyzer.unusedSourceClips.Count})", true, EditorStyles.boldLabel);
                if (_unusedSourcesExpanded) {
                    if (analyzer.unusedSourceClips.Count > 0) {
                        GUILayout.Space(4f);
                        foreach (var cn in analyzer.unusedSourceClips) {
                            DrawCleanupEntry(cn);
                        }
                    } else {
                        EditorGUILayout.LabelField("All source assets are in use", EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(12f);

            // Unused Library Section
            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                _unusedLibraryExpanded = EditorGUILayout.Foldout(_unusedLibraryExpanded, $"Unused in Library folder ({analyzer.unusedLibraryClips.Count})", true, EditorStyles.boldLabel);
                if (_unusedLibraryExpanded) {
                    if (analyzer.unusedLibraryClips.Count > 0) {
                        GUILayout.Space(4f);
                        foreach (var cn in analyzer.unusedLibraryClips) {
                            DrawCleanupEntry(cn);
                        }
                    } else {
                        EditorGUILayout.LabelField("All library assets are referenced", EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawCleanupEntry(ZoundDependencyAnalyzer.ClipNode cn) {
            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                PlayableClip(cn.assetPath, EditorStyles.label, cn.fileName);
                EditorGUILayout.LabelField(cn.assetPath, EditorStyles.miniLabel);
                
                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(35f))) {
                    if (cn.clip != null) EditorGUIUtility.PingObject(cn.clip);
                }

                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(50f))) {
                    if (EditorUtility.DisplayDialog("Delete Asset?", $"Are you sure you want to permanently delete this audio file?\n\n{cn.assetPath}", "Delete", "Cancel")) {
                        AssetDatabase.DeleteAsset(cn.assetPath);
                        RefreshAnalysis();
                    }
                }
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();
        }

        // ── Section: Build ──────────────────────────────────────────────

        private bool _staleExpanded = true;
        private bool _missingAddrExpanded = true;
        private bool _validExpanded = false;

        private void DrawBuildStatus() {
            if (buildReport == null) {
                buildReport = ZoundsBuildReport.Generate(analyzer);
            }

            var report = buildReport;

            // ── Summary box ──
            int totalShouldShip = report.analyzer.buildClips.Count;
            int readyToShip = report.validEntries.Count;
            int notYetAddressable = report.missingEntries.Count;

            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("Build Readiness Report", EditorStyles.boldLabel);
                GUILayout.Space(4f);

                if (!report.hasIssues) {
                    var prev = GUI.color;
                    GUI.color = new Color(0.5f, 1f, 0.5f);
                    EditorGUILayout.LabelField("CLEAN — Addressables match project state.", EditorStyles.boldLabel);
                    GUI.color = prev;
                } else {
                    var prev = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.3f);
                    var parts = new List<string>();
                    if (notYetAddressable > 0) parts.Add($"{notYetAddressable} not Addressable");
                    if (report.staleEntries.Count > 0) parts.Add($"{report.staleEntries.Count} stale");
                    if (report.invalidEntries.Count > 0) parts.Add($"{report.invalidEntries.Count} invalid");
                    if (report.hasBrokenRefs) parts.Add($"{report.brokenRefCount} broken refs");
                    if (report.orphanCount > 0) parts.Add($"{report.orphanCount} orphans");
                    EditorGUILayout.LabelField($"ISSUES: {string.Join(", ", parts)}", EditorStyles.boldLabel);
                    GUI.color = prev;
                }

                GUILayout.Space(6f);

                // Folder breakdown from analyzer
                var ps = ZoundsProject.Instance.projectSettings;
                int sourceOutputCount = 0;
                int renderedCount = 0;
                int libraryCount = 0;
                int totalSourcesOnDisk = report.analyzer.clipNodes.Values.Count(c =>
                    c.assetPath.StartsWith(ps.sourcesFolderPath));
                foreach (var cn in report.analyzer.buildClips) {
                    if (cn.assetPath.StartsWith(ps.sourcesFolderPath)) sourceOutputCount++;
                    else if (cn.assetPath.StartsWith(ps.workFolderPath) || cn.assetPath.StartsWith(ps.zoundFilesFolderPath)) renderedCount++;
                    else if (cn.assetPath.StartsWith(ps.libraryFolderPath)) libraryCount++;
                }
                int sourcesExcluded = totalSourcesOnDisk - sourceOutputCount;

                EditorGUILayout.LabelField("Should Ship", $"{totalShouldShip} clips", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Sources (as output)", $"{sourceOutputCount} clips", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Rendered", $"{renderedCount} clips", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Library", $"{libraryCount} clips", EditorStyles.miniLabel);
                if (sourcesExcluded > 0) {
                    EditorGUILayout.LabelField("Sources excluded", $"{sourcesExcluded} clips (not used as output)", EditorStyles.miniLabel);
                }

                GUILayout.Space(4f);
                EditorGUILayout.LabelField("Addressables", $"{readyToShip} ready", EditorStyles.miniLabel);
                if (notYetAddressable > 0) {
                    var prev2 = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.3f);
                    EditorGUILayout.LabelField("  Not Addressable", $"{notYetAddressable} (fix with Reconcile)", EditorStyles.miniLabel);
                    GUI.color = prev2;
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            // ── Collect covered Zounds from ALL clips that should ship (valid + missing) ──
            var coveredZoundIds = new HashSet<int>();
            foreach (var cn in report.analyzer.buildClips) {
                CollectCoveredZounds(cn.assetPath, report.analyzer, coveredZoundIds);
            }

            // ── All clips that should ship (from analyzer, the single source of truth) ──
            if (totalShouldShip > 0) {
                // Build a unified list: valid entries have their ClipEntry, missing entries too
                // We iterate analyzer.buildClips and mark which are addressable
                var addressablePaths = new HashSet<string>();
                foreach (var v in report.validEntries) addressablePaths.Add(v.assetPath);

                GUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    GUILayout.BeginHorizontal();
                    {
                        _validExpanded = EditorGUILayout.Foldout(_validExpanded,
                            $"Clip List ({totalShouldShip})", true, EditorStyles.boldLabel);
                        if (_validExpanded) {
                            string toggleLabel = _allShippingExpanded ? "Collapse All" : "Expand All";
                            if (GUILayout.Button(toggleLabel, EditorStyles.miniButton, GUILayout.Width(80f))) {
                                _allShippingExpanded = !_allShippingExpanded;
                                if (!_allShippingExpanded) _expandedShippingClips.Clear();
                            }
                        }
                    }
                    GUILayout.EndHorizontal();

                    if (_validExpanded) {
                        foreach (var cn in report.analyzer.buildClips) {
                            bool isAddressable = addressablePaths.Contains(cn.assetPath);
                            var clipEntry = new ZoundsBuildReport.ClipEntry {
                                guid = AssetDatabase.AssetPathToGUID(cn.assetPath),
                                assetPath = cn.assetPath,
                                fileName = cn.fileName
                            };
                            DrawShippingClipEntry(clipEntry, report.analyzer, coveredZoundIds, isAddressable);
                        }
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(4f);
            }

            // ── Uncovered Zounds check ──
            DrawUncoveredZounds(report.analyzer, coveredZoundIds);

            // ── Discrepancies ──
            if (report.hasDiscrepancies) {
                if (report.staleEntries.Count > 0) {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        _staleExpanded = EditorGUILayout.Foldout(_staleExpanded,
                            $"Stale — in Addressables but ShouldBeAddressable=false ({report.staleEntries.Count})", true, EditorStyles.boldLabel);
                        if (_staleExpanded) {
                            foreach (var entry in report.staleEntries) {
                                GUILayout.BeginHorizontal();
                                {
                                    EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);
                                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(35f))) {
                                        var asset = AssetDatabase.LoadAssetAtPath<Object>(entry.assetPath);
                                        if (asset != null) EditorGUIUtility.PingObject(asset);
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }
                        }
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(4f);
                }

                if (report.missingEntries.Count > 0) {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        _missingAddrExpanded = EditorGUILayout.Foldout(_missingAddrExpanded,
                            $"Missing — ShouldBeAddressable=true but not in group ({report.missingEntries.Count})", true, EditorStyles.boldLabel);
                        if (_missingAddrExpanded) {
                            foreach (var entry in report.missingEntries) {
                                GUILayout.BeginHorizontal();
                                {
                                    EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);
                                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(35f))) {
                                        var asset = AssetDatabase.LoadAssetAtPath<Object>(entry.assetPath);
                                        if (asset != null) EditorGUIUtility.PingObject(asset);
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }
                        }
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(4f);
                }

                if (report.invalidEntries.Count > 0) {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        GUI.color = new Color(1f, 0.4f, 0.4f);
                        EditorGUILayout.LabelField($"Invalid Entries ({report.invalidEntries.Count})", EditorStyles.boldLabel);
                        GUI.color = Color.white;
                        EditorGUILayout.LabelField("Non-AudioClip assets in the Zounds Addressable group:", MiniLabelWrap);
                        foreach (var entry in report.invalidEntries) {
                            EditorGUILayout.LabelField($"  {entry.fileName} (GUID: {entry.guid})", EditorStyles.miniLabel);
                        }
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(4f);
                }
            }

            // ── Cross-references ──
            if (report.hasBrokenRefs || report.orphanCount > 0) {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.LabelField("Related Issues", EditorStyles.boldLabel);
                    if (report.hasBrokenRefs) {
                        GUILayout.BeginHorizontal();
                        {
                            GUI.color = new Color(1f, 0.4f, 0.4f);
                            EditorGUILayout.LabelField($"  {report.brokenRefCount} Zound(s) with broken audio references", EditorStyles.miniLabel);
                            GUI.color = Color.white;
                            if (GUILayout.Button("Go to Broken tab", EditorStyles.miniButton, GUILayout.Width(110f))) {
                                activeSection = Section.BrokenZounds;
                                scrollPos = Vector2.zero;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (report.orphanCount > 0) {
                        GUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField($"  {report.orphanCount} orphaned file(s) in Work/ZoundFiles", EditorStyles.miniLabel);
                            if (GUILayout.Button("Go to Orphans tab", EditorStyles.miniButton, GUILayout.Width(110f))) {
                                activeSection = Section.Orphans;
                                scrollPos = Vector2.zero;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(4f);
            }

            GUILayout.Space(8f);

            // ── Actions ──
            GUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("Refresh Report", GUILayout.Height(24f))) {
                    RefreshAnalysis();
                    buildReport = ZoundsBuildReport.Generate(analyzer);
                }

                if (report.hasDiscrepancies) {
                    int fixCount = report.staleEntries.Count + report.missingEntries.Count + report.invalidEntries.Count;
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                    if (GUILayout.Button($"Reconcile ({fixCount} fixes)", GUILayout.Height(24f))) {
                        report.Reconcile();
                        RefreshAnalysis();
                        buildReport = ZoundsBuildReport.Generate(analyzer);
                    }
                    GUI.backgroundColor = prevBg;
                }
            }
            GUILayout.EndHorizontal();
        }

        // ── Build Status helpers ────────────────────────────────────────

        private HashSet<string> _expandedShippingClips = new HashSet<string>();
        private bool _allShippingExpanded = false;

        private void DrawShippingClipEntry(ZoundsBuildReport.ClipEntry entry, ZoundDependencyAnalyzer analyzerSnapshot, HashSet<int> coveredZoundIds, bool isAddressable = true) {
            bool expanded = _allShippingExpanded || _expandedShippingClips.Contains(entry.assetPath);

            var allDependents = GetAllClipDependents(entry.assetPath, analyzerSnapshot);

            foreach (var z in allDependents) {
                coveredZoundIds.Add(z.id);
            }

            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.BeginHorizontal();
                {
                    string depCount = allDependents.Count > 0 ? $" ({allDependents.Count} zounds)" : " (library — available by name)";
                    string addrTag = isAddressable ? "" : " <color=#FFBB44>[NOT ADDRESSABLE]</color>";
                    string label = $"<b>{entry.fileName}</b>  <color=#AAAAAA>{depCount}</color>{addrTag}";
                    bool newExpanded = EditorGUILayout.Foldout(expanded, label, true, FoldoutRich);
                    if (newExpanded != expanded && !_allShippingExpanded) {
                        if (newExpanded) _expandedShippingClips.Add(entry.assetPath);
                        else _expandedShippingClips.Remove(entry.assetPath);
                    }

                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(35f))) {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(entry.assetPath);
                        if (asset != null) EditorGUIUtility.PingObject(asset);
                    }
                }
                GUILayout.EndHorizontal();

                if (expanded) {
                    PlayableClip(entry.assetPath, EditorStyles.miniLabel, entry.assetPath);
                    if (allDependents.Count > 0) {
                        EditorGUILayout.LabelField("Full dependency chain:", EditorStyles.miniBoldLabel);
                        foreach (var z in allDependents) {
                            string locality = z.parentId != 0 ? " <color=#888888>(local)</color>" : "";
                            GUILayout.BeginHorizontal();
                            PlayableZound(z, LabelRich, true, "  ");
                            if (!string.IsNullOrEmpty(locality)) {
                                EditorGUILayout.LabelField(locality, LabelRich, GUILayout.Width(50f));
                            }
                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
            GUILayout.EndVertical();
        }

        private void CollectCoveredZounds(string clipPath, ZoundDependencyAnalyzer analyzerSnapshot, HashSet<int> coveredZoundIds) {
            foreach (var z in GetAllClipDependents(clipPath, analyzerSnapshot)) {
                coveredZoundIds.Add(z.id);
            }
        }

        /// <summary>
        /// Returns all Zounds that depend on a clip: direct referencedBy + transitive dependents
        /// (Klip -> parent Zeq -> grandparent Zeq, and also parentId chain for local Zounds).
        /// </summary>
        private static List<Zound> GetAllClipDependents(string clipPath, ZoundDependencyAnalyzer analyzerSnapshot) {
            if (!analyzerSnapshot.clipNodes.TryGetValue(clipPath, out var clipNode)) {
                return new List<Zound>();
            }

            var result = new HashSet<int>();
            var resultList = new List<Zound>();

            foreach (var directZound in clipNode.referencedBy) {
                // Add the direct dependent
                if (result.Add(directZound.id)) {
                    resultList.Add(directZound);
                }

                // Walk up parentId chain (local Klip -> parent Zeq)
                var current = directZound;
                while (current.parentId != 0) {
                    if (!result.Add(current.parentId)) break;
                    var parent = ZoundsProject.Instance.zoundLibrary.FindZound(p => p.id == current.parentId);
                    if (parent == null) break;
                    resultList.Add(parent);
                    current = parent;
                }

                // Walk up dependedOnBy chain (top-level Klip used as entry in a Zeq)
                foreach (var transitive in analyzerSnapshot.GetTransitiveDependents(directZound)) {
                    if (result.Add(transitive.id)) {
                        resultList.Add(transitive);
                    }
                }
            }

            return resultList;
        }

        private bool _uncoveredExpanded = true;
        private HashSet<int> _expandedUncoveredIds = new HashSet<int>();

        private void DrawUncoveredZounds(ZoundDependencyAnalyzer analyzerSnapshot, HashSet<int> coveredZoundIds) {
            // Find all top-level Zounds (not ClipZound, not local children) that are NOT covered
            var uncovered = new List<Zound>();
            foreach (var kvp in analyzerSnapshot.zoundNodes) {
                var node = kvp.Value;
                if (node.zound is ClipZound) continue;
                if (node.zound.parentId != 0) continue;
                if (!coveredZoundIds.Contains(node.zound.id)) {
                    uncovered.Add(node.zound);
                }
            }

            if (uncovered.Count == 0) return;

            GUILayout.Space(4f);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.7f, 0.3f);
                _uncoveredExpanded = EditorGUILayout.Foldout(_uncoveredExpanded,
                    $"Uncovered Zounds ({uncovered.Count}) — no shipping clip backs these", true, EditorStyles.boldLabel);
                GUI.color = prev;

                if (_uncoveredExpanded) {
                    EditorGUILayout.LabelField(
                        "These Zounds exist in the project but none of their audio clips are in the shipping set. " +
                        "They will fail to play at runtime.", MiniLabelWrap);
                    GUILayout.Space(4f);
                    foreach (var z in uncovered.OrderBy(z => z.name)) {
                        DrawUncoveredZoundEntry(z, analyzerSnapshot);
                    }
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawUncoveredZoundEntry(Zound z, ZoundDependencyAnalyzer analyzerSnapshot) {
            bool expanded = _expandedUncoveredIds.Contains(z.id);
            bool isBroken = analyzerSnapshot.zoundNodes.TryGetValue(z.id, out var node) && node.isBroken;
            string typeTag = z is Klip ? "Klip" : z is Zequence ? "Zeq" : "?";
            string color = isBroken ? "#FF6666" : "#FFBB44";
            string suffix = isBroken ? " <color=#FF6666>(broken ref)</color>" : "";

            GUILayout.BeginVertical(EditorStyles.helpBox);
            {
                string label = $"<color={color}>[{typeTag}]</color> <b>{z.name}</b>{suffix}";
                bool newExpanded = EditorGUILayout.Foldout(expanded, label, true, FoldoutRich);
                if (newExpanded != expanded) {
                    if (newExpanded) _expandedUncoveredIds.Add(z.id);
                    else _expandedUncoveredIds.Remove(z.id);
                }

                if (newExpanded && node != null) {
                    EditorGUI.indentLevel++;
                    DrawUncoveredDependencyTree(z, analyzerSnapshot, 0);
                    EditorGUI.indentLevel--;
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawUncoveredDependencyTree(Zound z, ZoundDependencyAnalyzer analyzerSnapshot, int depth) {
            if (depth > 10) return;

            if (!analyzerSnapshot.zoundNodes.TryGetValue(z.id, out var node)) return;

            if (z is Klip klip) {
                DrawKlipClipInfo(klip);
            }
            else if (z is Zequence zeq) {
                if (zeq.renderedClipRef != null && !string.IsNullOrEmpty(zeq.renderedClipRef.AssetGUID)) {
                    DrawClipRefLine("rendered", zeq.renderedClipPath, zeq.renderedClipRef, false);
                }
            }

            if (node.dependsOn.Count > 0) {
                foreach (var child in node.dependsOn) {
                    if (child is Klip childKlip && TryDrawKlipInline(childKlip)) {
                        continue;
                    }

                    PlayableZound(child);
                    EditorGUI.indentLevel++;
                    DrawUncoveredDependencyTree(child, analyzerSnapshot, depth + 1);
                    EditorGUI.indentLevel--;
                }
            }
        }

        /// <summary>
        /// Shows the clip a Klip will load at runtime, matching GetAudioClipReference() logic:
        /// - No destructive edits: source IS the output clip.
        /// - Destructive edits + rendered exists: rendered clip is the output.
        /// - Destructive edits + no rendered: source used as fallback (edits not applied).
        /// </summary>
        private void DrawKlipClipInfo(Klip klip) {
            bool hasEdits = klip.HasActiveEdits();

            if (!hasEdits) {
                // No edits — source clip is the output clip, one row
                DrawClipRefLine("clip (source = output)", klip.audioClipPath, klip.audioClipRef, true);
            } else {
                // Has edits — source and output are different
                DrawClipRefLine("source", klip.audioClipPath, klip.audioClipRef, false);
                bool hasRendered = klip.renderedClipRef != null && klip.renderedClipRef.RuntimeKeyIsValid()
                    && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(klip.renderedClipRef.AssetGUID));
                if (hasRendered) {
                    DrawClipRefLine("output (rendered)", klip.renderedClipPath, klip.renderedClipRef, true);
                } else {
                    EditorGUILayout.LabelField($"  <color=#FFBB44>output: needs render (using source as fallback)</color>", LabelRich);
                }
            }
        }

        private void DrawClipRefLine(string label, string clipPath, AssetReference clipRef, bool isCritical) {
            bool missing = clipRef == null || string.IsNullOrEmpty(clipRef.AssetGUID);
            if (!missing) {
                string resolved = AssetDatabase.GUIDToAssetPath(clipRef.AssetGUID);
                missing = string.IsNullOrEmpty(resolved) || AssetDatabase.LoadAssetAtPath<AudioClip>(resolved) == null;
            }

            if (missing) {
                string display = !string.IsNullOrEmpty(clipPath) ? System.IO.Path.GetFileName(clipPath) : "(no reference)";
                string color = isCritical ? "#FF6666" : "#FF6666";
                EditorGUILayout.LabelField($"  <color={color}>{label}: {display} — MISSING</color>", LabelRich);
            } else {
                string path = AssetDatabase.GUIDToAssetPath(clipRef.AssetGUID);
                GUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField($"  <color=#AAAAAA>{label}:</color>", LabelRich, GUILayout.Width(100f));
                    PlayableClip(path);
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(35f))) {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                        if (asset != null) EditorGUIUtility.PingObject(asset);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // ── Shared drawing helpers ──────────────────────────────────────

        // ── Unified playable label ──────────────────────────────────────

        /// <summary>
        /// Draws a clickable label for a Zound. Click plays the Zound.
        /// Handles rich text, type tag prefix, and local name trimming.
        /// </summary>
        /// <summary>
        /// Color coding: local Zounds are gray, shared (public) are blue.
        /// </summary>
        private static string ZoundNameColor(Zound zound) {
            return zound.parentId != 0 ? "#888888" : "#88AAFF";
        }

        private void PlayableZound(Zound zound, GUIStyle style = null, bool showTypeTag = true, string extraPrefix = "") {
            if (style == null) style = LabelRich;
            string color = ZoundNameColor(zound);
            string typeTag = showTypeTag ? $"<color={color}>[{GetTypeLabel(zound)}]</color> " : "";
            string displayName = GetCleanZoundName(zound);
            string label = $"{extraPrefix}{typeTag}<color={color}>{displayName}</color>";

            var rect = GUILayoutUtility.GetRect(new GUIContent(label), style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (GUI.Button(rect, label, style)) {
                ZoundEngine.PlayZound(zound);
            }
        }

        /// <summary>
        /// Draws a Klip as a single inline row when it has no edits (source = output).
        /// Format: [Klip] KlipName (filename.wav) — click name to play Zound, click filename to preview clip.
        /// Returns true if drawn inline, false if caller should use DrawKlipClipInfo for the full display.
        /// </summary>
        private bool TryDrawKlipInline(Klip klip) {
            if (klip.HasActiveEdits()) return false;
            if (klip.audioClipRef == null || string.IsNullOrEmpty(klip.audioClipRef.AssetGUID)) return false;
            string clipPath = AssetDatabase.GUIDToAssetPath(klip.audioClipRef.AssetGUID);
            if (string.IsNullOrEmpty(clipPath)) return false;

            string color = ZoundNameColor(klip);
            string fileName = System.IO.Path.GetFileName(clipPath);
            string typeTag = $"<color={color}>[Klip]</color> ";
            string nameLabel = $"<color={color}>{GetCleanZoundName(klip)}</color>";
            string clipLabel = $"<color=#AAAAAA>({fileName})</color>";

            GUILayout.BeginHorizontal();
            {
                // Playable Zound name
                string fullLabel = $"{typeTag}{nameLabel} {clipLabel}";
                var rect = GUILayoutUtility.GetRect(new GUIContent(fullLabel), LabelRich);
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
                if (GUI.Button(rect, fullLabel, LabelRich)) {
                    ZoundEngine.PlayZound(klip);
                }
            }
            GUILayout.EndHorizontal();
            return true;
        }

        /// <summary>
        /// Draws a clickable label for an AudioClip path. Click plays the clip preview.
        /// </summary>
        private void PlayableClip(string clipPath, GUIStyle style = null, string labelOverride = null) {
            if (style == null) style = LabelRich;
            string display = labelOverride ?? System.IO.Path.GetFileName(clipPath);

            var rect = GUILayoutUtility.GetRect(new GUIContent(display), style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (GUI.Button(rect, display, style)) {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip != null) {
                    AudioPreviewUtility.PlayPreviewClip(clip);
                }
            }
        }

        private static string GetCleanZoundName(Zound zound) {
            string displayName = zound.name;
            if (zound.parentId != 0) {
                var parent = ZoundsProject.Instance.zoundLibrary.FindZound(z => z.id == zound.parentId);
                if (parent != null && displayName.StartsWith($"[{parent.name}]_")) {
                    displayName = displayName.Substring($"[{parent.name}]_".Length);
                }
            }
            return displayName;
        }

        private static string GetTypeLabel(Zound z) {
            if (z is Klip) return "Klip";
            if (z is Zequence) return "Zeq";
            if (z is ClipZound) return "AC";
            return "?";
        }
    }
}
#endif
