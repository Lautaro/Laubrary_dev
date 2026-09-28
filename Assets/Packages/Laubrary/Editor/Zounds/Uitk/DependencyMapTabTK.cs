#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Zounds window's Dependency Map tab (T-0470): the section toolbar (Dependencies,
    /// Broken, Orphans, Build) with Refresh, then the chosen section — the dependency browser (a foldout per top-level
    /// zound with its dependency tree and users), the broken-file groups with their fix row, the orphan / unused cleanup
    /// lists, and the build readiness report. The analysis, the build report and every action are the old tab's shared
    /// code; the content is rebuilt when the analysis or a foldout changes (the analysis refreshes every 5 s as before).
    /// </summary>
    internal class DependencyMapTabTK : VisualElement {

        enum Section { DependencyBrowser, BrokenZounds, Orphans, BuildStatus }

        const double RefreshIntervalSeconds = 5.0;
        const float Indent = 15f;   // EditorGUI.indentLevel step

        readonly ZoundsWindowTK win;
        ZoundDependencyAnalyzer analyzer;
        ZoundsBuildReport buildReport;
        double lastAnalysisTime;
        string sig;

        static Section s_section = Section.DependencyBrowser;
        static string s_search = "";
        static readonly HashSet<int> s_expanded = new HashSet<int>();
        static bool s_orphansExpanded = true, s_unusedSourcesExpanded = true, s_unusedLibraryExpanded;
        static bool s_staleExpanded = true, s_missingAddrExpanded = true, s_validExpanded, s_allShippingExpanded, s_uncoveredExpanded = true;
        static readonly HashSet<string> s_expandedShipping = new HashSet<string>();
        static readonly HashSet<int> s_expandedUncovered = new HashSet<int>();

        readonly VisualElement toolbar;
        ScrollView scroll;
        TextField search;

        public DependencyMapTabTK(ZoundsWindowTK win) {
            this.win = win;
            style.flexGrow = 1; style.flexShrink = 1;
            RefreshAnalysis();
            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.style.flexGrow = 1; box.style.flexShrink = 1;
            Add(box);
            box.Add(ZequenceEditorWindowTK.Space(4f));
            toolbar = new VisualElement();
            toolbar.AddToClassList("zs-depbar");
            toolbar.style.flexDirection = FlexDirection.Row; toolbar.style.flexShrink = 0;
            box.Add(toolbar);
            box.Add(ZequenceEditorWindowTK.Space(2f));
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-depscroll");
            scroll.style.flexGrow = 1; scroll.style.flexShrink = 1;
            box.Add(scroll);
            Rebuild();
        }

        void RefreshAnalysis() {
            analyzer = ZoundDependencyAnalyzer.Analyze();
            lastAnalysisTime = EditorApplication.timeSinceStartup;
            buildReport = null;
        }

        string Signature() {
            var sb = new System.Text.StringBuilder();
            sb.Append((int)s_section).Append(analyzer.brokenZounds.Count).Append(',').Append(analyzer.orphanClips.Count).Append(',')
              .Append(analyzer.buildClips.Count).Append(',').Append(analyzer.unusedSourceClips.Count).Append(',').Append(analyzer.unusedLibraryClips.Count).Append('|');
            foreach (var n in analyzer.zoundNodes.Values) sb.Append(n.zound.id).Append(n.zound.name).Append(n.isBroken ? 'B' : 'b').Append(n.dependsOn.Count).Append(';');
            return sb.ToString();
        }

        public void Tick() {
            if (EditorApplication.timeSinceStartup - lastAnalysisTime > RefreshIntervalSeconds) {
                RefreshAnalysis();
                if (Signature() != sig) Rebuild();
            }
        }

        void Rebuild() {
            sig = Signature();
            BuildToolbar();
            float y = scroll.scrollOffset.y;
            scroll.Clear();
            switch (s_section) {
                case Section.DependencyBrowser: DependencyBrowser(scroll); break;
                case Section.BrokenZounds: Broken(scroll); break;
                case Section.Orphans: Orphans(scroll); break;
                case Section.BuildStatus: Build(scroll); break;
            }
            scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, y));
        }

        void Select(Section s) {
            if (s == s_section) return;
            s_section = s;
            scroll.scrollOffset = Vector2.zero;
            Rebuild();
        }

        // ─────────────────────────── toolbar ───────────────────────────

        void BuildToolbar() {
            toolbar.Clear();
            SectionButton(Section.DependencyBrowser, "Dependencies");
            SectionButton(Section.BrokenZounds, $"Broken ({analyzer.brokenZounds.Count})");
            SectionButton(Section.Orphans, $"Orphans ({analyzer.orphanClips.Count})");
            SectionButton(Section.BuildStatus, $"Build ({analyzer.buildClips.Count})");
            toolbar.Add(ZequenceEditorWindowTK.Flex());
            var refresh = MiniButton("Refresh", 60f, () => { RefreshAnalysis(); Rebuild(); });
            refresh.AddToClassList("zs-depbar__refresh");
            toolbar.Add(refresh);
        }

        /// <summary>DrawSectionButton: a toolbar button, the chosen one tinted blue, Broken red while anything is broken,
        /// Build amber while the build report has issues (GUI.backgroundColor multiplies the button's own background).</summary>
        void SectionButton(Section section, string label) {
            bool active = s_section == section;
            var t = new ToolbarToggle { text = label, value = active };
            t.AddToClassList("zs-depbar__button");
            t.RegisterValueChangedCallback(e => { if (e.newValue) Select(section); else t.SetValueWithoutNotify(true); });
            Color? tint = null;
            if (active) tint = new Color(0.6f, 0.8f, 1f);
            else if (section == Section.BrokenZounds && analyzer.brokenZounds.Count > 0) tint = new Color(1f, 0.4f, 0.4f);
            else if (section == Section.BuildStatus && buildReport != null && buildReport.hasIssues) tint = new Color(1f, 0.8f, 0.3f);
            if (tint.HasValue) t.AddToClassList(active ? "zs-depbar__button--active" : section == Section.BrokenZounds ? "zs-depbar__button--broken" : "zs-depbar__button--issues");
            toolbar.Add(t);
        }

        // ─────────────────────────── building blocks (the EditorGUILayout calls the old tab makes) ───────────────────────────

        static Button MiniButton(string text, float width, System.Action onClick) {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("zs-minibtn");
            if (width > 0f) b.style.width = width;
            return b;
        }

        static Label Rich(string text, string cls = "zs-lbl", int indent = 0) {
            var l = new Label(text) { enableRichText = true };
            l.AddToClassList("zs-lbl"); if (cls != "zs-lbl") l.AddToClassList(cls);
            l.AddToClassList("zs-deplabel");
            if (indent > 0) l.style.paddingLeft = indent * Indent + 1f;
            return l;
        }

        static VisualElement HelpBox(bool row = false) {
            var v = new VisualElement();
            v.AddToClassList("zs-dephelp");
            if (row) v.style.flexDirection = FlexDirection.Row;
            return v;
        }

        static VisualElement Row() {
            var r = new VisualElement();
            r.AddToClassList("zs-deprow");
            r.style.flexDirection = FlexDirection.Row; r.style.flexShrink = 0; r.style.alignItems = Align.FlexStart;
            return r;
        }

        /// <summary>EditorGUILayout.Foldout with a rich label, toggled by clicking the label too.</summary>
        VisualElement FoldoutRow(string richLabel, bool expanded, System.Action<bool> toggled, string cls = null) {
            var f = new Foldout { text = richLabel, value = expanded };
            f.AddToClassList("zs-depfoldout");
            f.AddToClassList("zs-deprow");
            if (cls != null) f.AddToClassList(cls);
            var lbl = f.Q<Label>();
            if (lbl != null) lbl.enableRichText = true;
            f.RegisterValueChangedCallback(e => { if (e.target == f) { toggled(e.newValue); Rebuild(); } });
            f.style.flexGrow = 1;
            return f;
        }

        /// <summary>A clickable label: GUI.Button over a label rect. A plain GUI.Button ignores EditorGUI.indentLevel, so
        /// these never indent (unlike the LabelFields beside them) — kept, as that is how the old tab looks.</summary>
        static Label Clickable(string richText, System.Action onClick, string cls = "zs-lbl", int indent = 0) {
            var l = Rich(richText, cls, 0);
            l.AddToClassList("zs-link");
            l.RegisterCallback<ClickEvent>(_ => onClick());
            l.style.flexGrow = 0; l.style.flexShrink = 1;
            return l;
        }

        static void Ping(string path) {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        // PlayableZound: "[Klip] name" in the zound's colour (grey local, blue shared); click plays it.
        static Label PlayableZound(Zound zound, bool showTypeTag = true, string extraPrefix = "", int indent = 0) {
            string color = DependencyMapTab.ZoundNameColor(zound);
            string typeTag = showTypeTag ? $"<color={color}>[{DependencyMapTab.GetTypeLabel(zound)}]</color> " : "";
            return Clickable($"{extraPrefix}{typeTag}<color={color}>{DependencyMapTab.GetCleanZoundName(zound)}</color>", () => ZoundEngine.PlayZound(zound), "zs-lbl", indent);
        }

        static Label PlayableClip(string clipPath, string cls = "zs-lbl", string labelOverride = null, int indent = 0) {
            string display = labelOverride ?? System.IO.Path.GetFileName(clipPath);
            return Clickable(display, () => {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip != null) AudioPreviewUtility.PlayPreviewClip(clip);
            }, cls, indent);
        }

        // ─────────────────────────── Dependencies ───────────────────────────

        void DependencyBrowser(VisualElement into) {
            var r = Row();
            r.AddToClassList("zs-depsearch");
            var l = Rich("Search:"); l.style.width = 50f; l.style.height = 18f;
            r.Add(l);
            search = new TextField { value = s_search };
            search.AddToClassList("zs-imgui-field");
            search.style.flexGrow = 1; search.style.flexShrink = 1;
            search.RegisterValueChangedCallback(e => { s_search = e.newValue; RebuildNodes(); });
            r.Add(search);
            var x = new Button(() => { s_search = ""; search.SetValueWithoutNotify(""); search.Blur(); RebuildNodes(); }) { text = "X" };
            x.AddToClassList("zs-imgui-button"); x.style.width = 20f;
            r.Add(x);
            into.Add(r);
            into.Add(ZequenceEditorWindowTK.Space(4f));
            nodesHost = new VisualElement();
            into.Add(nodesHost);
            RebuildNodes();
        }

        VisualElement nodesHost;

        void RebuildNodes() {
            nodesHost.Clear();
            string filter = s_search.ToLowerInvariant();
            var nodes = analyzer.zoundNodes.Values.Where(n => !(n.zound is ClipZound)).Where(n => n.zound.parentId == 0).OrderBy(n => n.zound.name).ToList();
            if (!string.IsNullOrEmpty(filter)) nodes = nodes.Where(n => n.zound.name.ToLowerInvariant().Contains(filter)).ToList();
            if (nodes.Count == 0) { var none = Rich("No Zounds match the filter.", "zs-greymini"); none.style.height = 18f; nodesHost.Add(none); return; }
            foreach (var n in nodes) nodesHost.Add(ZoundNode(n));
        }

        VisualElement ZoundNode(ZoundDependencyAnalyzer.ZoundNode node) {
            var z = node.zound;
            bool expanded = s_expanded.Contains(z.id);
            var box = HelpBox();
            var head = Row();
            string typeLabel = DependencyMapTab.GetTypeLabel(z);
            string color = node.isBroken ? "#FF6666" : DependencyMapTab.ZoundNameColor(z);
            string clipSuffix = "";
            if (z is Klip fk && !fk.HasActiveEdits() && fk.audioClipRef != null && !string.IsNullOrEmpty(fk.audioClipRef.AssetGUID)) {
                string clipPath = AssetDatabase.GUIDToAssetPath(fk.audioClipRef.AssetGUID);
                if (!string.IsNullOrEmpty(clipPath)) clipSuffix = $" <color=#AAAAAA>({System.IO.Path.GetFileName(clipPath)})</color>";
            }
            head.Add(FoldoutRow($"<color={color}>[{typeLabel}]</color> <b>{DependencyMapTab.GetCleanZoundName(z)}</b>{clipSuffix}", expanded,
                                v => { if (v) s_expanded.Add(z.id); else s_expanded.Remove(z.id); }));
            head.Add(MiniButton("Play", 35f, () => ZoundEngine.PlayZound(z)));
            box.Add(head);
            if (expanded) {
                if (node.isBroken) {
                    var br = Rich(node.brokenReason, "zs-mini"); br.AddToClassList("zs-wrap");
                    br.style.color = new Color(1f * 0.824f, 0.4f * 0.824f, 0.4f * 0.824f);
                    box.Add(br);
                }
                box.Add(Rich("Dependencies:", "zs-minibold"));
                DependencyTree(box, z, 0, 0);
                var users = analyzer.GetTransitiveDependents(z).Where(d => d.parentId == 0 && !(d is ClipZound)).OrderBy(d => d.name).ToList();
                if (users.Count > 0) {
                    box.Add(ZequenceEditorWindowTK.Space(4f));
                    box.Add(Rich("Used by:", "zs-minibold"));
                    foreach (var d in users) { var rr = Row(); rr.Add(PlayableZound(d, true, "  ")); box.Add(rr); }
                }
            }
            return box;
        }

        void DependencyTree(VisualElement into, Zound z, int depth, int indent) {
            if (depth > 10) return;
            if (z is Klip klip) {
                if (klip.HasActiveEdits()) KlipClipInfo(into, klip, indent + 1);
            }
            else if (z is CompositeZound composite) {
                int ind = indent + 1;
                if (z is Zequence zeq && zeq.renderedClipRef != null && !string.IsNullOrEmpty(zeq.renderedClipRef.AssetGUID))
                    ClipRefLine(into, "rendered", zeq.renderedClipPath, zeq.renderedClipRef, false, ind);
                foreach (var entry in composite.zoundEntries) {
                    if (!composite.TryGetEntryZound(entry, out var child) || child is ClipZound) continue;
                    bool childBroken = analyzer.zoundNodes.TryGetValue(child.id, out var cn) && cn.isBroken;
                    if (child is Klip ck && KlipInline(into, ck, ind, childBroken)) continue;
                    var pz = PlayableZound(child, true, "", ind);
                    if (childBroken) pz.style.unityBackgroundImageTintColor = new Color(1f, 0.5f, 0.5f);
                    if (childBroken) pz.style.color = new Color(1f * 0.769f, 0.5f * 0.769f, 0.5f * 0.769f);
                    into.Add(pz);
                    DependencyTree(into, child, depth + 1, ind);
                }
            }
        }

        bool KlipInline(VisualElement into, Klip klip, int indent, bool broken) {
            if (klip.HasActiveEdits()) return false;
            if (klip.audioClipRef == null || string.IsNullOrEmpty(klip.audioClipRef.AssetGUID)) return false;
            string clipPath = AssetDatabase.GUIDToAssetPath(klip.audioClipRef.AssetGUID);
            if (string.IsNullOrEmpty(clipPath)) return false;
            string color = DependencyMapTab.ZoundNameColor(klip);
            var l = Clickable($"<color={color}>[Klip]</color> <color={color}>{DependencyMapTab.GetCleanZoundName(klip)}</color> <color=#AAAAAA>({System.IO.Path.GetFileName(clipPath)})</color>",
                              () => ZoundEngine.PlayZound(klip), "zs-lbl", indent);
            if (broken) l.style.color = new Color(1f * 0.769f, 0.5f * 0.769f, 0.5f * 0.769f);
            var r = Row(); r.Add(l); into.Add(r);
            return true;
        }

        void KlipClipInfo(VisualElement into, Klip klip, int indent) {
            if (!klip.HasActiveEdits()) { ClipRefLine(into, "clip (source = output)", klip.audioClipPath, klip.audioClipRef, true, indent); return; }
            ClipRefLine(into, "source", klip.audioClipPath, klip.audioClipRef, false, indent);
            bool hasRendered = klip.renderedClipRef != null && klip.renderedClipRef.RuntimeKeyIsValid()
                               && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(klip.renderedClipRef.AssetGUID));
            if (hasRendered) ClipRefLine(into, "output (rendered)", klip.renderedClipPath, klip.renderedClipRef, true, indent);
            else into.Add(Rich("  <color=#FFBB44>output: needs render (using source as fallback)</color>", "zs-lbl", indent));
        }

        void ClipRefLine(VisualElement into, string label, string clipPath, AssetReference clipRef, bool isCritical, int indent) {
            bool missing = clipRef == null || string.IsNullOrEmpty(clipRef.AssetGUID);
            if (!missing) {
                string resolved = AssetDatabase.GUIDToAssetPath(clipRef.AssetGUID);
                missing = string.IsNullOrEmpty(resolved) || AssetDatabase.LoadAssetAtPath<AudioClip>(resolved) == null;
            }
            if (missing) {
                string display = !string.IsNullOrEmpty(clipPath) ? System.IO.Path.GetFileName(clipPath) : "(no reference)";
                into.Add(Rich($"  <color=#FF6666>{label}: {display} — MISSING</color>", "zs-lbl", indent));
                return;
            }
            string path = AssetDatabase.GUIDToAssetPath(clipRef.AssetGUID);
            var r = Row();
            var l = Rich($"  <color=#AAAAAA>{label}:</color>", "zs-lbl", indent); l.style.width = 100f; l.style.flexShrink = 0; l.style.marginRight = 3.5f;
            r.Add(l);
            r.Add(PlayableClip(path));
            r.Add(ZequenceEditorWindowTK.Flex());
            r.Add(MiniButton("Ping", 35f, () => Ping(path)));
            into.Add(r);
        }

        // ─────────────────────────── Broken ───────────────────────────

        void Broken(VisualElement into) {
            if (analyzer.brokenGroups.Count == 0) {
                into.Add(ZequenceEditorWindowTK.Space(20f));
                var l = Rich("No broken references detected.", "zs-greymini"); l.style.height = 18f;
                into.Add(l);
                return;
            }
            var title = Rich($"{analyzer.brokenGroups.Count} missing file(s) identified:", "zs-bold"); title.style.height = 18f;
            into.Add(title);
            into.Add(ZequenceEditorWindowTK.Space(4f));
            foreach (var kvp in analyzer.brokenGroups) {
                var group = kvp.Value;
                var box = HelpBox();
                string fileName = System.IO.Path.GetFileName(group.missingKey);
                if (string.IsNullOrEmpty(fileName)) fileName = group.missingKey;
                var head = Rich($"MISSING: {fileName}", "zs-bold"); head.style.color = new Color(0.769f, 0.4f * 0.769f, 0.4f * 0.769f);
                box.Add(head);
                box.Add(Rich(group.missingKey, "zs-mini"));
                box.Add(ZequenceEditorWindowTK.Space(4f));
                box.Add(Rich("Referenced by:", "zs-minibold"));
                foreach (var root in group.affectedHierarchy) {
                    var inner = HelpBox();
                    inner.Add(Rich($"<b>{root.Key}</b>"));
                    foreach (var child in root.Value) {
                        string display = string.IsNullOrEmpty(child.Key) ? "" : child.Key;
                        foreach (var slot in child.Value) { if (!string.IsNullOrEmpty(display)) display += " / "; display += slot; }
                        inner.Add(Rich($"  <color=#FF4444>•</color> {display}"));
                    }
                    box.Add(inner);
                }
                box.Add(ZequenceEditorWindowTK.Space(8f));
                var fix = HelpBox(true);
                var field = new ObjectField("Replace with:") { objectType = typeof(AudioClip), allowSceneObjects = false, value = group.stagedFix };
                field.AddToClassList("zs-imgui-field"); field.AddToClassList("zs-label-150");
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(e => { group.stagedFix = e.newValue as AudioClip; Rebuild(); });
                fix.Add(field);
                if (group.stagedFix != null) {
                    var all = new Button(() => { if (DependencyMapTab.ApplyGroupFixShared(group)) { RefreshAnalysis(); Rebuild(); } }) { text = "FIX ALL" };
                    all.AddToClassList("zs-imgui-button"); all.AddToClassList("zs-fixall");
                    all.style.width = 80f;
                    fix.Add(all);
                    var clear = new Button(() => { group.stagedFix = null; Rebuild(); }) { text = "X" };
                    clear.AddToClassList("zs-imgui-button"); clear.style.width = 20f;
                    fix.Add(clear);
                }
                box.Add(fix);
                into.Add(box);
                into.Add(ZequenceEditorWindowTK.Space(4f));
            }
        }

        // ─────────────────────────── Orphans ───────────────────────────

        void Orphans(VisualElement into) {
            {
                var box = HelpBox();
                var head = Row();
                head.Add(FoldoutRow($"Orphans ({analyzer.orphanClips.Count})", s_orphansExpanded, v => s_orphansExpanded = v, "zs-depfoldout--bold"));
                if (s_orphansExpanded && analyzer.orphanClips.Count > 0) {
                    var del = new Button(() => {
                        var paths = analyzer.orphanClips.Select(c => c.assetPath).ToList();
                        if (EditorUtility.DisplayDialog("Delete Orphans",
                                $"Delete {paths.Count} orphaned file(s)?\n\n{string.Join("\n", paths.Take(15))}" + (paths.Count > 15 ? $"\n...and {paths.Count - 15} more" : ""),
                                "Delete", "Cancel")) {
                            foreach (var p in paths) AssetDatabase.DeleteAsset(p);
                            RefreshAnalysis(); Rebuild();
                        }
                    }) { text = "Delete All Orphans" };
                    del.AddToClassList("zs-imgui-button"); del.style.width = 130f;
                    head.Add(del);
                }
                box.Add(head);
                if (s_orphansExpanded) CleanupList(box, analyzer.orphanClips, "All Work/Rendered files are in use");
                into.Add(box);
            }
            into.Add(ZequenceEditorWindowTK.Space(12f));
            {
                var box = HelpBox();
                box.Add(FoldoutRow($"Unused in Source folder ({analyzer.unusedSourceClips.Count})", s_unusedSourcesExpanded, v => s_unusedSourcesExpanded = v, "zs-depfoldout--bold"));
                if (s_unusedSourcesExpanded) CleanupList(box, analyzer.unusedSourceClips, "All source assets are in use");
                into.Add(box);
            }
            into.Add(ZequenceEditorWindowTK.Space(12f));
            {
                var box = HelpBox();
                box.Add(FoldoutRow($"Unused in Library folder ({analyzer.unusedLibraryClips.Count})", s_unusedLibraryExpanded, v => s_unusedLibraryExpanded = v, "zs-depfoldout--bold"));
                if (s_unusedLibraryExpanded) CleanupList(box, analyzer.unusedLibraryClips, "All library assets are referenced");
                into.Add(box);
            }
        }

        void CleanupList(VisualElement box, List<ZoundDependencyAnalyzer.ClipNode> clips, string emptyText) {
            if (clips.Count == 0) { var l = Rich(emptyText, "zs-greymini"); l.style.height = 18f; box.Add(l); return; }
            box.Add(ZequenceEditorWindowTK.Space(4f));
            foreach (var cn in clips) {
                var row = HelpBox(true);
                row.Add(PlayableClip(cn.assetPath, "zs-lbl", cn.fileName));
                var path = Rich(cn.assetPath, "zs-mini"); path.style.flexGrow = 1; path.style.flexShrink = 1;
                row.Add(path);
                row.Add(MiniButton("Ping", 35f, () => { if (cn.clip != null) EditorGUIUtility.PingObject(cn.clip); }));
                var del = MiniButton("Delete", 50f, () => {
                    if (EditorUtility.DisplayDialog("Delete Asset?", $"Are you sure you want to permanently delete this audio file?\n\n{cn.assetPath}", "Delete", "Cancel")) {
                        AssetDatabase.DeleteAsset(cn.assetPath);
                        RefreshAnalysis(); Rebuild();
                    }
                });
                del.AddToClassList("zs-minibtn--danger");
                row.Add(del);
                box.Add(row);
            }
        }

        // ─────────────────────────── Build ───────────────────────────

        void Build(VisualElement into) {
            if (buildReport == null) buildReport = ZoundsBuildReport.Generate(analyzer);
            var report = buildReport;
            int totalShouldShip = report.analyzer.buildClips.Count, readyToShip = report.validEntries.Count, notYetAddressable = report.missingEntries.Count;

            var summary = HelpBox();
            summary.Add(Rich("Build Readiness Report", "zs-bold"));
            summary.Add(ZequenceEditorWindowTK.Space(4f));
            if (!report.hasIssues) {
                var ok = Rich("CLEAN — Addressables match project state.", "zs-bold"); ok.style.color = new Color(0.5f * 0.769f, 0.769f, 0.5f * 0.769f);
                summary.Add(ok);
            }
            else {
                var parts = new List<string>();
                if (notYetAddressable > 0) parts.Add($"{notYetAddressable} not Addressable");
                if (report.staleEntries.Count > 0) parts.Add($"{report.staleEntries.Count} stale");
                if (report.invalidEntries.Count > 0) parts.Add($"{report.invalidEntries.Count} invalid");
                if (report.hasBrokenRefs) parts.Add($"{report.brokenRefCount} broken refs");
                if (report.orphanCount > 0) parts.Add($"{report.orphanCount} orphans");
                var issues = Rich($"ISSUES: {string.Join(", ", parts)}", "zs-bold"); issues.style.color = new Color(0.769f, 0.8f * 0.769f, 0.3f * 0.769f);
                summary.Add(issues);
            }
            summary.Add(ZequenceEditorWindowTK.Space(6f));
            var ps = ZoundsProject.Instance.projectSettings;
            int sourceOutput = 0, rendered = 0, library = 0;
            int totalSources = report.analyzer.clipNodes.Values.Count(c => c.assetPath.StartsWith(ps.sourcesFolderPath));
            foreach (var cn in report.analyzer.buildClips) {
                if (cn.assetPath.StartsWith(ps.sourcesFolderPath)) sourceOutput++;
                else if (cn.assetPath.StartsWith(ps.workFolderPath) || cn.assetPath.StartsWith(ps.zoundFilesFolderPath)) rendered++;
                else if (cn.assetPath.StartsWith(ps.libraryFolderPath)) library++;
            }
            int excluded = totalSources - sourceOutput;
            summary.Add(Pair("Should Ship", $"{totalShouldShip} clips"));
            summary.Add(Pair("  Sources (as output)", $"{sourceOutput} clips"));
            summary.Add(Pair("  Rendered", $"{rendered} clips"));
            summary.Add(Pair("  Library", $"{library} clips"));
            if (excluded > 0) summary.Add(Pair("Sources excluded", $"{excluded} clips (not used as output)"));
            summary.Add(ZequenceEditorWindowTK.Space(4f));
            summary.Add(Pair("Addressables", $"{readyToShip} ready"));
            if (notYetAddressable > 0) { var na = Pair("  Not Addressable", $"{notYetAddressable} (fix with Reconcile)"); na.Query<Label>().ForEach(l => l.style.color = new Color(0.824f, 0.8f * 0.824f, 0.3f * 0.824f)); summary.Add(na); }
            into.Add(summary);
            into.Add(ZequenceEditorWindowTK.Space(8f));

            var covered = new HashSet<int>();
            foreach (var cn in report.analyzer.buildClips) DependencyMapTab.CollectCoveredZounds(cn.assetPath, report.analyzer, covered);

            if (totalShouldShip > 0) {
                var addressable = new HashSet<string>(report.validEntries.Select(v => v.assetPath));
                var box = HelpBox();
                var head = Row();
                head.Add(FoldoutRow($"Clip List ({totalShouldShip})", s_validExpanded, v => s_validExpanded = v, "zs-depfoldout--bold"));
                if (s_validExpanded)
                    head.Add(MiniButton(s_allShippingExpanded ? "Collapse All" : "Expand All", 80f, () => {
                        s_allShippingExpanded = !s_allShippingExpanded;
                        if (!s_allShippingExpanded) s_expandedShipping.Clear();
                        Rebuild();
                    }));
                box.Add(head);
                if (s_validExpanded)
                    foreach (var cn in report.analyzer.buildClips)
                        box.Add(ShippingClip(cn.assetPath, cn.fileName, report.analyzer, covered, addressable.Contains(cn.assetPath)));
                into.Add(box);
                into.Add(ZequenceEditorWindowTK.Space(4f));
            }

            Uncovered(into, report.analyzer, covered);

            if (report.hasDiscrepancies) {
                if (report.staleEntries.Count > 0)
                    into.Add(PathList($"Stale — in Addressables but ShouldBeAddressable=false ({report.staleEntries.Count})", s_staleExpanded, v => s_staleExpanded = v,
                                      report.staleEntries.Select(e => e.assetPath)));
                if (report.missingEntries.Count > 0)
                    into.Add(PathList($"Missing — ShouldBeAddressable=true but not in group ({report.missingEntries.Count})", s_missingAddrExpanded, v => s_missingAddrExpanded = v,
                                      report.missingEntries.Select(e => e.assetPath)));
                if (report.invalidEntries.Count > 0) {
                    var box = HelpBox();
                    var t = Rich($"Invalid Entries ({report.invalidEntries.Count})", "zs-bold"); t.style.color = new Color(0.769f, 0.4f * 0.769f, 0.4f * 0.769f);
                    box.Add(t);
                    var d = Rich("Non-AudioClip assets in the Zounds Addressable group:", "zs-mini"); d.AddToClassList("zs-wrap");
                    box.Add(d);
                    foreach (var e in report.invalidEntries) box.Add(Rich($"  {e.fileName} (GUID: {e.guid})", "zs-mini"));
                    into.Add(box);
                    into.Add(ZequenceEditorWindowTK.Space(4f));
                }
            }

            if (report.hasBrokenRefs || report.orphanCount > 0) {
                var box = HelpBox();
                box.Add(Rich("Related Issues", "zs-bold"));
                if (report.hasBrokenRefs) {
                    var r = Row();
                    var l = Rich($"  {report.brokenRefCount} Zound(s) with broken audio references", "zs-mini"); l.style.flexGrow = 1;
                    l.style.color = new Color(0.824f, 0.4f * 0.824f, 0.4f * 0.824f);
                    r.Add(l);
                    r.Add(MiniButton("Go to Broken tab", 110f, () => Select(Section.BrokenZounds)));
                    box.Add(r);
                }
                if (report.orphanCount > 0) {
                    var r = Row();
                    var l = Rich($"  {report.orphanCount} orphaned file(s) in Work/ZoundFiles", "zs-mini"); l.style.flexGrow = 1;
                    r.Add(l);
                    r.Add(MiniButton("Go to Orphans tab", 110f, () => Select(Section.Orphans)));
                    box.Add(r);
                }
                into.Add(box);
                into.Add(ZequenceEditorWindowTK.Space(4f));
            }

            into.Add(ZequenceEditorWindowTK.Space(8f));
            var actions = Row();
            var refresh = new Button(() => { RefreshAnalysis(); buildReport = ZoundsBuildReport.Generate(analyzer); Rebuild(); }) { text = "Refresh Report" };
            refresh.AddToClassList("zs-imgui-button"); refresh.style.height = 24f; refresh.style.flexGrow = 1; refresh.style.flexBasis = 0;
            actions.Add(refresh);
            if (report.hasDiscrepancies) {
                int fixCount = report.staleEntries.Count + report.missingEntries.Count + report.invalidEntries.Count;
                var reconcile = new Button(() => { report.Reconcile(); RefreshAnalysis(); buildReport = ZoundsBuildReport.Generate(analyzer); Rebuild(); }) { text = $"Reconcile ({fixCount} fixes)" };
                reconcile.AddToClassList("zs-imgui-button"); reconcile.AddToClassList("zs-reconcile");
                reconcile.style.height = 24f; reconcile.style.flexGrow = 1; reconcile.style.flexBasis = 0;
                actions.Add(reconcile);
            }
            into.Add(actions);
            // Generating the report is what lets the section button show amber; bring the toolbar up to date.
            BuildToolbar();
        }

        /// <summary>EditorGUILayout.LabelField(label, value, miniLabel): the label in the editor's label column, then the value.</summary>
        static VisualElement Pair(string label, string value) {
            var r = Row();
            var a = Rich(label); a.AddToClassList("zs-paircol");   // the label column in the editor's label style, the value mini
            var b = Rich(value, "zs-mini"); b.style.flexGrow = 1;
            r.Add(a); r.Add(b);
            return r;
        }

        VisualElement PathList(string title, bool expanded, System.Action<bool> toggled, IEnumerable<string> paths) {
            var box = HelpBox();
            box.Add(FoldoutRow(title, expanded, toggled, "zs-depfoldout--bold"));
            if (expanded)
                foreach (var p in paths) {
                    var r = Row();
                    var l = Rich(p, "zs-mini"); l.style.flexGrow = 1;
                    r.Add(l);
                    r.Add(MiniButton("Ping", 35f, () => Ping(p)));
                    box.Add(r);
                }
            var wrap = new VisualElement(); wrap.Add(box); wrap.Add(ZequenceEditorWindowTK.Space(4f));
            return wrap;
        }

        VisualElement ShippingClip(string assetPath, string fileName, ZoundDependencyAnalyzer an, HashSet<int> covered, bool isAddressable) {
            bool expanded = s_allShippingExpanded || s_expandedShipping.Contains(assetPath);
            var deps = DependencyMapTab.GetAllClipDependents(assetPath, an);
            foreach (var z in deps) covered.Add(z.id);
            var box = HelpBox();
            var head = Row();
            string depCount = deps.Count > 0 ? $" ({deps.Count} zounds)" : " (library — available by name)";
            string addrTag = isAddressable ? "" : " <color=#FFBB44>[NOT ADDRESSABLE]</color>";
            head.Add(FoldoutRow($"<b>{fileName}</b>  <color=#AAAAAA>{depCount}</color>{addrTag}", expanded, v => {
                if (s_allShippingExpanded) return;
                if (v) s_expandedShipping.Add(assetPath); else s_expandedShipping.Remove(assetPath);
            }));
            head.Add(MiniButton("Ping", 35f, () => Ping(assetPath)));
            box.Add(head);
            if (expanded) {
                box.Add(PlayableClip(assetPath, "zs-mini", assetPath));
                if (deps.Count > 0) {
                    box.Add(Rich("Full dependency chain:", "zs-minibold"));
                    foreach (var z in deps) {
                        var r = Row();
                        r.Add(PlayableZound(z, true, "  "));
                        if (z.parentId != 0) { var loc = Rich(" <color=#888888>(local)</color>"); loc.style.width = 50f; r.Add(loc); }
                        box.Add(r);
                    }
                }
            }
            return box;
        }

        void Uncovered(VisualElement into, ZoundDependencyAnalyzer an, HashSet<int> covered) {
            var uncovered = an.zoundNodes.Values.Where(n => !(n.zound is ClipZound) && n.zound.parentId == 0 && !covered.Contains(n.zound.id)).Select(n => n.zound).ToList();
            if (uncovered.Count == 0) return;
            into.Add(ZequenceEditorWindowTK.Space(4f));
            var box = HelpBox();
            box.Add(FoldoutRow($"Uncovered Zounds ({uncovered.Count}) — no shipping clip backs these", s_uncoveredExpanded, v => s_uncoveredExpanded = v, "zs-depfoldout--uncovered"));
            if (s_uncoveredExpanded) {
                var d = Rich("These Zounds exist in the project but none of their audio clips are in the shipping set. They will fail to play at runtime.", "zs-mini");
                d.AddToClassList("zs-wrap");
                box.Add(d);
                box.Add(ZequenceEditorWindowTK.Space(4f));
                foreach (var z in uncovered.OrderBy(z => z.name)) {
                    bool expanded = s_expandedUncovered.Contains(z.id);
                    bool isBroken = an.zoundNodes.TryGetValue(z.id, out var node) && node.isBroken;
                    string typeTag = z is Klip ? "Klip" : z is Zequence ? "Zeq" : "?";
                    string color = isBroken ? "#FF6666" : "#FFBB44";
                    string suffix = isBroken ? " <color=#FF6666>(broken ref)</color>" : "";
                    var entry = HelpBox();
                    entry.Add(FoldoutRow($"<color={color}>[{typeTag}]</color> <b>{z.name}</b>{suffix}", expanded, v => { if (v) s_expandedUncovered.Add(z.id); else s_expandedUncovered.Remove(z.id); }));
                    if (expanded && node != null) UncoveredTree(entry, z, an, 0, 1);
                    box.Add(entry);
                }
            }
            into.Add(box);
        }

        void UncoveredTree(VisualElement into, Zound z, ZoundDependencyAnalyzer an, int depth, int indent) {
            if (depth > 10) return;
            if (!an.zoundNodes.TryGetValue(z.id, out var node)) return;
            if (z is Klip klip) KlipClipInfo(into, klip, indent);
            else if (z is Zequence zeq && zeq.renderedClipRef != null && !string.IsNullOrEmpty(zeq.renderedClipRef.AssetGUID))
                ClipRefLine(into, "rendered", zeq.renderedClipPath, zeq.renderedClipRef, false, indent);
            foreach (var child in node.dependsOn) {
                if (child is Klip ck && KlipInline(into, ck, indent, false)) continue;
                into.Add(PlayableZound(child, true, "", indent));
                UncoveredTree(into, child, an, depth + 1, indent + 1);
            }
        }
    }
}
#endif
