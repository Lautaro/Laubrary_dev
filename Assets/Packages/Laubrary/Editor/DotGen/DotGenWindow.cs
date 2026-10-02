// DotGenWindow — the authoring window for a DotGen document.
//
// Shaped exactly like PyreWindow: a section toggle bar across the top, a fixed-width column-flow pane of
// dials on the left, and the picture on the right with its own backdrop. That is deliberate — a Laubrary
// tool should be operable by someone who already learned another one, so the shell is copied rather than
// reinvented and only the dials differ.
//
// Split across partials:
//   DotGenWindow.cs         — shell, Undo/dirty plumbing, Frame / Hierarchy / Generator sections
//   DotGenWindow.Preview.cs — the IMGUI preview island: navigation, backdrop, chrome row, legend
//   DotGenWindow.Cards.cs   — the Placement / Selectors / Mutators / Drawers card lists (W2.2)
//   DotGenWindow.Gizmos.cs  — the process overlays drawn over the frame (W2.3)

using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.DotGen.Editor
{
    public partial class DotGenWindow : ZuiAssetWindow<DotGen>
    {
        [MenuItem("Laubrary/DotGen")]
        public static void Open() => GetWindow<DotGenWindow>("DotGen");

        /// Open the window ON a particular document — the entry point a reference chip's "Edit" needs.
        /// Not a menu item: DotGen has exactly one.
        public static void OpenFor(DotGen document)
        {
            var w = GetWindow<DotGenWindow>("DotGen");
            if (document != null) w.SetAsset(document);
        }

        DotGen doc => Current;

        protected override string TypeLabel => "DotGen";
        protected override string NewAssetName => "New DotGen";
        protected override string DefaultFolder => "Assets/DotGen";

        /// A document is a still picture, so the browser thumbnail is simply that picture, small.
        protected override Texture2D RenderThumbnail(DotGen item)
        {
            if (item == null) return null;
            item.Normalize();
            var res = DotGenEvaluator.Evaluate(item);
            return DotGenRenderer.Render(item, res, ThumbnailSize, withDots: true);
        }

        const int ThumbnailSize = 128;
        protected override bool AnimateThumbnails => false;

        /// A brand new document is the demonstration composition, not an empty frame: a generator system that
        /// shows nothing on the first screen teaches nothing about itself.
        protected override void InitializeNewAsset(DotGen item)
        {
            if (item == null) return;
            item.ApplyDemo();
        }

        protected override void OnAssetChanged()
        {
            ResetView();
            InvalidateRender();
        }

        // ── layout state (window-instance, persisted across domain reloads) ──────────────────
        [SerializeField] float leftPaneWidth = 360f;
        const float LeftPaneMin = 360f;
        const float LeftPaneMax = 4f * 360f + 3f * 6f;
        ScrollView leftPane;

        // Sections held so the toggle bar can drive them and so their bodies can be refilled in place.
        ZuiSection viewsSection, frameSection, hierarchySection, generatorSection;
        VisualElement generatorHost, treeHost, hierarchyButtonsHost;
        Label frameReadout;
        IntegerField seedField;

        // Tree rows' metadata lines, refreshed from the evaluation result instead of rebuilt, so dragging a
        // dial never churns the row the pointer is over.
        readonly List<(string id, Label meta, Label count)> treeMeta = new List<(string, Label, Label)>();

        // ── Undo / dirty ─────────────────────────────────────────────────────────────────────

        /// Every authored edit goes through here. `Undo.RecordObject` BEFORE the mutation, the asset marked
        /// dirty after it, and the picture invalidated — three things that must never come apart.
        void Dirty(Action apply, string label = "Edit DotGen")
        {
            if (doc == null || apply == null) return;
            Undo.RecordObject(doc, label);
            apply();
            EditorUtility.SetDirty(doc);
            MarkDirty();
        }

        /// The same, for an edit that changes only how the document is LOOKED at (never the picture) — it
        /// repaints without throwing away the evaluated result and the rendered frame.
        void DirtyRepaintOnly(Action apply, string label = "Edit DotGen")
        {
            if (doc == null || apply == null) return;
            Undo.RecordObject(doc, label);
            apply();
            EditorUtility.SetDirty(doc);
            preview?.MarkDirtyRepaint();
        }

        /// The picture is stale. Evaluation and rendering happen lazily on the next preview repaint, so a
        /// slider drag costs one render per frame drawn rather than one per pointer move.
        void MarkDirty()
        {
            InvalidateRender();
            preview?.MarkDirtyRepaint();
        }

        /// The record half of the Undo contract, for controls that bracket their own drag gesture
        /// (ZuiMicroSlider does; passing this as its `onBeforeMutate` is what collapses a drag into one step).
        Action Record(string label) => () => { if (doc != null) Undo.RecordObject(doc, label); };

        /// The commit half: what a bracketed control calls once its value has been written.
        void Applied()
        {
            if (doc != null) EditorUtility.SetDirty(doc);
            MarkDirty();
        }

        /// A bounded scalar dial wired for one-undo-per-drag and double-click-restores-the-default.
        ZuiMicroSlider Dial(string label, float value, float min, float max, string tooltip,
            Action<float> set, float defaultValue, int decimals = -1, float width = 150f)
            => Z.MicroSlider(label, value, min, max, tooltip,
                v => { set(v); Applied(); }, width,
                defaultValue: defaultValue, decimals: decimals, onBeforeMutate: Record("Edit DotGen"));

        ZuiMicroSlider DialInt(string label, int value, int min, int max, string tooltip,
            Action<int> set, int defaultValue, float width = 150f)
            => Dial(label, value, min, max, tooltip, v => set(Mathf.RoundToInt(v)), defaultValue, 0, width);

        // Undo/redo and asset switches rebuild the whole window through ZuiWindow.Rebuild, which bypasses
        // Dirty — so the cached picture is invalidated here, or the preview would keep showing the pre-undo
        // frame after Ctrl+Z.
        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            InvalidateRender();
            treeMeta.Clear();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseRender();
        }

        // ── window ───────────────────────────────────────────────────────────────────────────

        protected override void BuildAsset(VisualElement root, DotGen d)
        {
            Z.AttachTool(root, "dotgen");
            // Repair anything the document could be missing (a root, a placement, an id) before anything reads
            // it. Idempotent, and it never overwrites a value the document already carries.
            d.Normalize();

            var split = new VisualElement();
            split.AddToClassList("lau-tool-shell__split");

            // ── left: the dials ──────────────────────────────────────────────────
            var left = new ScrollView(ScrollViewMode.Vertical);
            leftPane = left;
            left.style.width = Mathf.Clamp(leftPaneWidth, LeftPaneMin, LeftPaneMax);
            left.AddToClassList("lau-tool-shell__side--resizable");
            // Nothing in this pane is ever wider than the pane, so the horizontal scroller is an inert stub —
            // and a horizontal scrollbar on a pane meant to fit is read as a layout bug, not as chrome.
            left.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            left.contentContainer.AddToClassList("lau-tool-shell__column");

            // One width-driven column flow: a single 360px column that splits into more as the pane widens.
            // Each Build* below adds EXACTLY ONE top-level unit, in reading order.
            var flow = Z.ColumnFlow(360f);
            left.contentContainer.Add(flow);

            var viewBar = BuildViewBar(root);
            viewsSection = Z.Section("Views", "Save and recall named presets of which boxes are folded open.",
                "dotgen.views");
            viewsSection.Add(viewBar);
            flow.Add(viewsSection);

            BuildFrame(flow, d);
            BuildHierarchy(flow, d);
            BuildGenerator(flow, d);
            BuildCards(flow, d);   // DotGenWindow.Cards.cs — Placement / Selectors / Mutators / Drawers

            // ── right: the picture ───────────────────────────────────────────────
            var rightPane = new VisualElement();
            rightPane.AddToClassList("lau-tool-shell__pane");
            BuildPreviewPane(rightPane, d);   // DotGenWindow.Preview.cs

            split.Add(left);
            split.Add(BuildVerticalSplitter());
            split.Add(rightPane);

            root.AddToClassList("lau-tool-shell");

            // The toggle bar spans the whole window above everything else. TagsSection was already parented by
            // the base class; re-adding it here pulls it down to just below the bar, where it reads as one more
            // toggleable section (Add always detaches first).
            root.Add(BuildSectionToggleBar());
            if (TagsSection != null) root.Add(TagsSection);
            root.Add(split);

            RefillSelectionSections();
            viewBar.RestoreLast();
        }

        VisualElement BuildSectionToggleBar()
            => new ZuiSectionToggleBar("DotGen",
                ("Tags", TagsSection),
                ("Views", viewsSection),
                ("Frame", frameSection),
                ("Hierarchy", hierarchySection),
                ("Generator", generatorSection),
                ("Placement", placementSection),
                ("Selectors", selectorsSection),
                ("Mutators", mutatorsSection),
                ("Drawers", drawersSection));

        VisualElement BuildVerticalSplitter()
        {
            var s = new VisualElement { tooltip = "Drag to resize the dial pane (wider = more control columns)." };
            s.AddToClassList("lau-tool-shell__resize-grip");
            s.AddToClassList("lau-tool-shell__resize-grip--vertical");
            s.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { s.CapturePointer(e.pointerId); e.StopPropagation(); } });
            s.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!s.HasPointerCapture(e.pointerId)) return;
                float cap = Mathf.Min(LeftPaneMax, Mathf.Max(LeftPaneMin, position.width - 260f));
                leftPaneWidth = Mathf.Clamp(leftPaneWidth + e.deltaPosition.x, LeftPaneMin, cap);
                if (leftPane != null) leftPane.style.width = leftPaneWidth;
                e.StopPropagation();
            });
            s.RegisterCallback<PointerUpEvent>(e => { if (s.HasPointerCapture(e.pointerId)) s.ReleasePointer(e.pointerId); });
            return s;
        }

        // ── saved views ──────────────────────────────────────────────────────────────────────
        const string ViewStorePath = "Assets/DotGen/DotGenViews.asset";
        const string ViewPrefsKey = "DotGen.lastView";

        ZuiViewBar BuildViewBar(VisualElement paneRoot)
        {
            Dictionary<string, bool> Capture()
            {
                var d = new Dictionary<string, bool>();
                foreach (var b in paneRoot.Query<ZuiBox>().ToList()) b.CaptureView(d);
                return d;
            }
            void Apply(IReadOnlyDictionary<string, bool> from)
            {
                foreach (var b in paneRoot.Query<ZuiBox>().ToList()) b.ApplyView(from);
            }
            return new ZuiViewBar(
                () => AssetDatabase.LoadAssetAtPath<ZuiViewStore>(ViewStorePath),
                CreateViewStore, Capture, Apply, ViewPrefsKey);
        }

        ZuiViewStore CreateViewStore()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DotGen"))
                AssetDatabase.CreateFolder("Assets", "DotGen");
            var store = ScriptableObject.CreateInstance<ZuiViewStore>();
            AssetDatabase.CreateAsset(store, ViewStorePath);
            Undo.RegisterCreatedObjectUndo(store, "Create DotGen Views");
            AssetDatabase.SaveAssets();
            return store;
        }

        // ── Frame section ────────────────────────────────────────────────────────────────────

        void BuildFrame(VisualElement host, DotGen d)
        {
            frameSection = Z.Section("Frame",
                "The fixed square everything is generated inside: its seed, its resolution, and the colours "
                + "drawn behind and around the composition.", "dotgen.frame");

            seedField = Z.Int(d.seed, "The one number every random draw in this document comes from. "
                + "The same seed always gives the same picture.",
                v => Dirty(() => d.seed = Mathf.Clamp(v, 0, 999998), "Set DotGen seed"), 60f);
            // Seed, its reroll and the resolution are three short document-wide dials — one row, not three.
            frameSection.Add(Z.Row(
                Z.Field("Seed", "The one number every random draw in this document comes from.", seedField),
                Z.Button("New seed", "Draw a different composition from the same settings.", NewSeed),
                Dial("Frame size", d.frameSize, 64f, 2048f,
                    "Resolution of the rendered frame, in pixels. Exports at this size too.",
                    v => d.frameSize = Mathf.RoundToInt(v), 512f, 0, 118f)));

            frameSection.Add(Z.Field("Background", "The colour behind everything.",
                Z.Color(d.background, "The colour behind everything.",
                    v => Dirty(() => d.background = v, "Set DotGen background"), 110f)));

            frameSection.Add(Z.Row(
                Z.Field("Guide grid", "The colour of the ten-by-ten guide grid.",
                    Z.Color(d.guideGrid, "The colour of the ten-by-ten guide grid.",
                        v => Dirty(() => d.guideGrid = v, "Set DotGen guide grid colour"), 110f)),
                Z.Toggle("Show", "Draws a ten-by-ten grid over the background, for judging placement.",
                    d.showGuideGrid, v => Dirty(() => d.showGuideGrid = v, "Toggle DotGen guide grid"))));

            frameSection.Add(Z.Row(
                Z.Field("Frame edge", "The colour of the border drawn around the frame.",
                    Z.Color(d.frameStroke, "The colour of the border drawn around the frame.",
                        v => Dirty(() => d.frameStroke = v, "Set DotGen frame edge colour"), 110f)),
                Z.Toggle("Show", "Draws a border around the frame, so its edge is visible against dark artwork.",
                    d.showFrameStroke, v => Dirty(() => d.showFrameStroke = v, "Toggle DotGen frame edge"))));

            frameSection.Add(Z.Field("Gizmos",
                "Which module's process overlays the preview draws over the picture.",
                Z.MiniRadio((int)d.gizmoMode, GizmoModeLabels,
                    "Which module's process overlays the preview draws over the picture. Hovered follows the "
                    + "card under the pointer; Selected sticks to the chosen one.",
                    v => DirtyRepaintOnly(() =>
                    {
                        d.gizmoMode = (DotGizmoMode)v;
                        RefreshChrome();
                    }, "Set DotGen gizmo mode"))));

            frameSection.Add(Z.Row(
                Z.Button("Export PNG…", "Write this document's picture to a PNG file, at the frame size above.",
                    ExportPng),
                Z.Button("Reset to demo",
                    "Replace the whole hierarchy with the demonstration composition (asks first; undoable).",
                    ResetToDemo)));

            // A permanently-reserved single line whose TEXT changes — never a line that grows from nothing and
            // shoves the buttons above it around.
            frameReadout = Z.Text("—", ZuiText.Subtle,
                "Dots that would be drawn, and how many generator areas were evaluated to produce them.");
            frameReadout.AddToClassList("lau-tool-shell__status-line");
            frameReadout.AddToClassList("lau-tool-shell__status-line--tall");
            frameSection.Add(frameReadout);

            host.Add(frameSection);
        }

        static readonly string[] GizmoModeLabels = { "Hovered", "Selected", "All", "Off" };

        /// A different draw of the same settings. Deliberately NOT `System.Random`/`UnityEngine.Random` — no
        /// ambient RNG lives in DotGen — so the one-off pick comes from the clock instead.
        void NewSeed()
        {
            if (doc == null) return;
            int next = (int)(DateTime.UtcNow.Ticks % 999999L);
            if (next == doc.seed) next = (next + 1) % 999999;
            Dirty(() => doc.seed = next, "New DotGen seed");
            seedField?.SetValueWithoutNotify(next);
        }

        void ResetToDemo()
        {
            if (doc == null) return;
            if (!EditorUtility.DisplayDialog("Reset DotGen",
                    "Replace this document's whole generator hierarchy with the demonstration composition?",
                    "Reset", "Cancel")) return;
            ApplyDemoReset();
        }

        /// The reset itself, split from the confirmation so the restore path can be exercised without a modal
        /// dialog. Undoable, like every other edit — which is why the dialog is a courtesy rather than a guard.
        void ApplyDemoReset()
        {
            if (doc == null) return;
            Dirty(() => doc.ApplyDemo(), "Reset DotGen to demo");
            Rebuild();
        }

        void ExportPng()
        {
            if (doc == null) return;
            string dir = AssetDatabase.GetAssetPath(doc);
            dir = string.IsNullOrEmpty(dir) ? "Assets" : Path.GetDirectoryName(dir).Replace('\\', '/');
            string path = EditorUtility.SaveFilePanel("Export DotGen PNG", dir,
                (doc.name ?? "DotGen") + ".png", "png");
            if (string.IsNullOrEmpty(path)) return;
            WritePng(path);
            EditorUtility.RevealInFinder(path);
        }

        /// The write itself, split from the save panel so the picture-on-disk path can be exercised without a
        /// modal dialog. Imports the result when it lands inside the project, so it shows up straight away.
        void WritePng(string path)
        {
            if (doc == null || string.IsNullOrEmpty(path)) return;
            File.WriteAllBytes(path, DotGenRenderer.RenderPng(doc, doc.frameSize));

            string projectRoot = Application.dataPath.Replace('\\', '/');
            projectRoot = projectRoot.Substring(0, projectRoot.Length - "Assets".Length);
            string full = path.Replace('\\', '/');
            if (full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.ImportAsset(full.Substring(projectRoot.Length));
        }

        // ── Hierarchy section ────────────────────────────────────────────────────────────────

        void BuildHierarchy(VisualElement host, DotGen d)
        {
            hierarchySection = Z.Section("Hierarchy",
                "Every generator in this document. A child attaches one area to each of its parent's surviving "
                + "dots, so the tree is what turns one lattice into a composition.", "dotgen.hierarchy");

            treeHost = new VisualElement();
            hierarchySection.Add(treeHost);

            // A fixed, never-wrapping row holding EVERY command at all times — what varies is whether they are
            // enabled, so nothing under the pointer moves when the selection changes.
            hierarchyButtonsHost = Z.Row();
            hierarchyButtonsHost.AddToClassList("zui-row--nowrap");
            hierarchySection.Add(hierarchyButtonsHost);

            host.Add(hierarchySection);
            RebuildTree();
        }

        void RebuildTree()
        {
            if (treeHost == null || doc == null) return;
            treeHost.Clear();
            treeMeta.Clear();

            var tree = doc.Tree();
            var flat = tree.Flatten();
            for (int i = 0; i < flat.Count; i++)
                treeHost.Add(BuildTreeRow(tree, flat[i]));

            RebuildHierarchyButtons();

            // Fill the fresh rows from the evaluation already in hand. Without this every rebuilt row reads
            // "—" until the preview next repaints, so clicking through the tree flashes the counts away and
            // back — the readouts are refreshed from the RENDER, which happens a frame later than the rows.
            if (result != null) RefreshReadouts(result);
        }

        VisualElement BuildTreeRow(DotGenTree tree, DotGenerator g)
        {
            bool selected = doc.selectedGeneratorId == g.id;
            int depth = tree.DepthOf(g);
            int childCount = tree.ChildrenOf(g).Count;

            var wrap = new VisualElement();
            wrap.style.paddingLeft = depth * 20f;
            wrap.EnableInClassList("lau-authoring__selected-row", selected);

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.AddToClassList("zui-row--nowrap");

            row.Add(Z.Toggle("", "Off stops this generator and everything below it: no areas, no dots, no "
                + "drawers, no children. Its settings are kept.", g.enabled,
                v => { Dirty(() => g.enabled = v, "Toggle generator"); RebuildTree(); }));

            var chip = new VisualElement
            {
                tooltip = "This generator's dot, legend and gizmo colour — set it in the Generator section."
            };
            chip.AddToClassList("zui-row__swatch");
            chip.AddToClassList("zui-row__swatch--round");
            chip.style.backgroundColor = g.color;
            row.Add(chip);

            var name = Z.Text(string.IsNullOrEmpty(g.name) ? "(unnamed)" : g.name, ZuiText.Body,
                "Click to edit this generator below.");
            name.AddToClassList("zui-row__title");
            name.AddToClassList("zui-audit-allow-stretch");
            row.Add(name);

            var count = Z.Text(childCount > 0 ? childCount + " ▾" : "", ZuiText.Small,
                "How many child generators hang off this one.");
            count.AddToClassList("zui-row__kind");
            row.Add(count);
            wrap.Add(row);

            var meta = Z.Text("—", ZuiText.Small,
                "This generator's placement, how many dots it currently produces, and how many drawers paint from them.");
            meta.AddToClassList("lau-tool-shell__status-line");
            meta.AddToClassList("zui-row__metadata");
            wrap.Add(meta);
            treeMeta.Add((g.id, meta, count));

            wrap.AddManipulator(new Clickable(() => SelectGenerator(g.id)));

            // Hovering a row is the one way to ask "where does THAT generator sit?" without leaving the one you
            // are editing — so it targets the Generator Area gizmo the same way a card targets its own.
            string gid = g.id;
            wrap.RegisterCallback<PointerEnterEvent>(_ => HoverGenerator(gid));
            wrap.RegisterCallback<PointerLeaveEvent>(_ => { if (hoveredGeneratorId == gid) HoverGenerator(null); });
            return wrap;
        }

        /// The generator whose area outline follows the pointer in Hovered mode. Window-only, never persisted:
        /// it is a property of looking at the document, like the card hover it sits beside.
        string hoveredGeneratorId;

        void HoverGenerator(string id)
        {
            if (hoveredGeneratorId == id) return;
            hoveredGeneratorId = id;
            if (doc != null && doc.gizmoMode == DotGizmoMode.Hovered) preview?.MarkDirtyRepaint();
        }

        void RebuildHierarchyButtons()
        {
            if (hierarchyButtonsHost == null || doc == null) return;
            hierarchyButtonsHost.Clear();

            var sel = doc.Selected;
            bool isRoot = sel == null || sel.IsRoot;
            int index = SiblingIndex(sel, out int siblingCount);

            var add = Z.Button("+ Child generator",
                "Attach a new generator to every surviving dot of the selected one.", AddChild);
            var up = Z.Button("▲", "Move this generator earlier among its siblings — earlier ones draw behind.",
                () => MoveSelected(-1));
            var down = Z.Button("▼", "Move this generator later among its siblings — later ones draw in front.",
                () => MoveSelected(1));
            var dup = Z.Button("Duplicate",
                "Copy this generator and everything below it, alongside the original.", DuplicateSelected);
            var del = Z.Button("Delete",
                "Remove this generator and everything below it (undoable).", DeleteSelected);

            up.SetEnabled(!isRoot && index > 0);
            down.SetEnabled(!isRoot && index >= 0 && index < siblingCount - 1);
            dup.SetEnabled(!isRoot);
            del.SetEnabled(!isRoot);

            hierarchyButtonsHost.Add(add);
            hierarchyButtonsHost.Add(up);
            hierarchyButtonsHost.Add(down);
            hierarchyButtonsHost.Add(dup);
            hierarchyButtonsHost.Add(del);
        }

        int SiblingIndex(DotGenerator g, out int siblingCount)
        {
            siblingCount = 0;
            if (doc == null || g == null) return -1;
            int found = -1;
            for (int i = 0; i < doc.generators.Count; i++)
            {
                var o = doc.generators[i];
                if (o == null || o.parentId != g.parentId) continue;
                if (o == g) found = siblingCount;
                siblingCount++;
            }
            return found;
        }

        /// Selection is document data (the window reopens on it) but not an authoring edit — putting it in the
        /// undo stack would make Ctrl+Z walk back through clicks instead of through changes.
        void SelectGenerator(string id)
        {
            if (doc == null || doc.selectedGeneratorId == id) return;
            doc.selectedGeneratorId = id;
            EditorUtility.SetDirty(doc);
            selectedModuleId = null;
            RebuildTree();
            RefillSelectionSections();
            preview?.MarkDirtyRepaint();
        }

        void AddChild()
        {
            if (doc == null) return;
            var parent = doc.Selected;
            if (parent == null) return;

            string newId = null;
            Dirty(() =>
            {
                var child = new DotGenerator { parentId = parent.id };
                child.ApplyDefaults(false);
                child.id = doc.NewId("gen");
                child.placementBank[0].id = doc.NewId("place");

                // A parent whose placement hands out cells wants a child that fills one; a parent that does not
                // wants a small satellite on every other dot. Two different jobs, two different starting points.
                var pp = parent.ActivePlacement;
                if (pp != null && pp.ProvidesCells)
                {
                    child.areaBasis = DotAreaBasis.Cell;
                    child.sizeX = 100f;
                    child.sizeY = 100f;
                    child.spawnEvery = 1;
                    child.shape = DotShape.Rectangle;
                }
                else
                {
                    child.areaBasis = DotAreaBasis.Parent;
                    child.sizeX = 18f;
                    child.sizeY = 18f;
                    child.spawnEvery = 2;
                }

                doc.generators.Add(child);
                doc.selectedGeneratorId = child.id;
                newId = child.id;
            }, "Add DotGen child generator");

            if (newId != null) selectedModuleId = null;
            RebuildTree();
            RefillSelectionSections();
        }

        void MoveSelected(int delta)
        {
            if (doc == null) return;
            var g = doc.Selected;
            if (g == null || g.IsRoot) return;

            // Sibling order is order of appearance among entries sharing a parent, so moving one is a swap of
            // the two siblings' positions in the flat list. Descendants are untouched: their own order among
            // THEIR siblings has not changed.
            var siblings = new List<int>();
            int here = -1;
            for (int i = 0; i < doc.generators.Count; i++)
            {
                var o = doc.generators[i];
                if (o == null || o.parentId != g.parentId) continue;
                if (o == g) here = siblings.Count;
                siblings.Add(i);
            }
            int target = here + delta;
            if (here < 0 || target < 0 || target >= siblings.Count) return;

            Dirty(() =>
            {
                int a = siblings[here], b = siblings[target];
                (doc.generators[a], doc.generators[b]) = (doc.generators[b], doc.generators[a]);
            }, "Reorder DotGen generator");

            RebuildTree();
            RefillSelectionSections();
        }

        void DuplicateSelected()
        {
            if (doc == null) return;
            var g = doc.Selected;
            if (g == null || g.IsRoot) return;

            Dirty(() =>
            {
                var tree = doc.Tree();
                var subtree = new List<DotGenerator>();
                tree.Walk(g, subtree);

                var idMap = new Dictionary<string, string>();
                var copies = new List<DotGenerator>();
                foreach (var src in subtree)
                {
                    var copy = DotGenClone.CloneGenerator(src);
                    copy.id = doc.NewId("gen");
                    idMap[src.id] = copy.id;
                    copy.name = (src.name ?? "") + " copy";

                    // Fresh ids for every module, and copied references reset to All dots — a reference that
                    // silently pointed at the ORIGINAL's selector would be a bug wearing a working UI.
                    foreach (var p in copy.placementBank) p.id = doc.NewId("place");
                    foreach (var s in copy.selectors) s.id = doc.NewId("sel");
                    foreach (var m in copy.mutators) { m.id = doc.NewId("mut"); m.selectorId = ""; }
                    foreach (var dr in copy.drawers) { dr.id = doc.NewId("draw"); dr.selectorId = ""; }

                    copies.Add(copy);
                }

                for (int i = 0; i < subtree.Count; i++)
                {
                    var src = subtree[i];
                    copies[i].parentId = idMap.TryGetValue(src.parentId, out var mapped) ? mapped : src.parentId;
                }

                doc.generators.AddRange(copies);
                doc.selectedGeneratorId = copies[0].id;
            }, "Duplicate DotGen generator");

            selectedModuleId = null;
            RebuildTree();
            RefillSelectionSections();
        }

        void DeleteSelected()
        {
            if (doc == null) return;
            var g = doc.Selected;
            if (g == null || g.IsRoot) return;
            string parentId = g.parentId;

            Dirty(() =>
            {
                var tree = doc.Tree();
                var subtree = new List<DotGenerator>();
                tree.Walk(g, subtree);
                foreach (var victim in subtree) doc.generators.Remove(victim);
                doc.selectedGeneratorId = parentId;
            }, "Delete DotGen generator");

            selectedModuleId = null;
            RebuildTree();
            RefillSelectionSections();
        }

        // ── Generator section — the Generator Area card ───────────────────────────────────────

        void BuildGenerator(VisualElement host, DotGen d)
        {
            generatorSection = Z.Section("Generator",
                "The selected generator's own area: what shape it is, how big, which of its nine points sits on "
                + "the thing it is attached to, and how its dots look.", "dotgen.generator");
            generatorHost = new VisualElement();
            generatorSection.Add(generatorHost);
            host.Add(generatorSection);
        }

        void RebuildGenerator()
        {
            if (generatorHost == null || doc == null) return;
            generatorHost.Clear();

            var g = doc.Selected;
            if (g == null)
            {
                generatorHost.Add(Z.Text("No generator selected.", ZuiText.Subtle,
                    "Pick a generator in the Hierarchy section to edit it here."));
                return;
            }

            bool isRoot = g.IsRoot;
            var tree = doc.Tree();
            var parent = tree.ParentOf(g);

            // Identity first: the name (a declaration, so a text field is right here and nowhere else), whether
            // it runs at all, and what its area is attached to.
            var nameField = Z.TextInput(g.name ?? "", "This generator's name in the hierarchy and the legend.",
                v =>
                {
                    Undo.RecordObject(doc, "Rename generator");
                    g.name = v;
                    EditorUtility.SetDirty(doc);
                    RefreshTreeNames();
                }, 150f);
            generatorHost.Add(Z.Row(
                Z.Field("Name", "This generator's name in the hierarchy and the legend.", nameField),
                Z.Toggle("Enabled", "Off stops this generator and everything below it: no areas, no dots, no "
                    + "drawers, no children. Its settings are kept.", g.enabled,
                    v => { Dirty(() => g.enabled = v, "Toggle generator"); RebuildTree(); })));

            // Where this generator's area lives is an explanation, not a heading — it belongs in the tooltips
            // of the controls that shape the area, not on screen where it is re-read on every visit.
            string areaTip = isRoot
                ? "The root's area sits inside the fixed frame itself — there is nothing above it to attach to. "
                : "One copy of this area is attached to every surviving dot of " + (parent?.name ?? "its parent") + ". ";

            generatorHost.Add(Z.Row(
                Z.Toggle("Dot output", "Off hides the dot markers only — those dots still spawn children and "
                    + "feed drawers.", g.showDots,
                    v => { Dirty(() => g.showDots = v, "Toggle dot output"); RefreshChrome(); }),
                Z.Field("Colour", "This generator's dot markers, legend entry and gizmos.",
                    Z.Color(g.color, "This generator's dot markers, legend entry and gizmos.",
                        v => { Dirty(() => g.color = v, "Set generator colour"); RebuildTree(); RefreshChrome(); }, 90f))));

            generatorHost.Add(Dial("Dot size", g.dotSize, 1f, 8f,
                "Radius of each dot marker, in pixels at the reference 900 px frame.",
                v => g.dotSize = v, isRoot ? 4f : 3f, 1));

            string shapeTip = areaTip + "The shape of that area — dots that fall outside it are not emitted.";
            generatorHost.Add(Z.Field("Shape", shapeTip,
                Z.Segmented((int)g.shape, ShapeLabels, shapeTip,
                    v => Dirty(() => g.shape = (DotShape)v, "Set generator shape"))));

            generatorHost.Add(Z.HGroup(
                Dial("Width", g.sizeX, 2f, isRoot ? 100f : 200f,
                    areaTip + "This is its width, as a percentage of whatever it is attached to.",
                    v => g.sizeX = v, isRoot ? 100f : 22f, 0),
                Dial("Height", g.sizeY, 2f, isRoot ? 100f : 600f,
                    areaTip + "This is its height, as a percentage of whatever it is attached to. "
                    + "A child may grow far past its basis.",
                    v => g.sizeY = v, isRoot ? 100f : 22f, 0)));

            string anchorTip = areaTip
                + "This is which point of the area sits on that attachment point. Bottom makes an area grow upward.";
            generatorHost.Add(Z.Field("Anchor", anchorTip,
                Z.AnchorGrid((int)g.anchor, anchorTip,
                    v => Dirty(() => g.anchor = (DotAnchor)v, "Set generator anchor"),
                    Record("Set generator anchor"))));

            generatorHost.Add(Dial("Rotation", g.rotation, -180f, 180f,
                "Rotation of the area, added to the rotation of whatever it is attached to.",
                v => g.rotation = v, 0f, 0));

            if (isRoot) return;

            // ── child-only ───────────────────────────────────────────────────────
            // A thin rule, not a heading: what this block does belongs in its controls' own tooltips.
            generatorHost.Add(Z.Divider(null,
                "How this generator picks which of its parent's dots to appear on, and what it takes its size from."));

            // "Placement cell" is only meaningful when the parent's placement hands out cells, so it is offered
            // only then — an option that silently does nothing is worse than an option that is not there.
            var pp = parent?.ActivePlacement;
            bool hasCells = pp != null && pp.ProvidesCells;
            string[] basisLabels = hasCells ? AreaBasisLabels : AreaBasisParentOnly;
            int basisIndex = hasCells ? (int)g.areaBasis : 0;
            string basisTip = hasCells
                ? "Take size and rotation from the parent's whole area, or from the exact cell around the parent dot."
                : "Takes size and rotation from the parent's whole area. " + (parent?.name ?? "The parent")
                  + "'s placement produces no cells, so there is nothing else to measure against.";
            generatorHost.Add(Z.Field("Size relative to", basisTip,
                Choice(basisIndex, basisLabels, basisTip,
                    v => Dirty(() => g.areaBasis = (DotAreaBasis)v, "Set area basis"))));

            generatorHost.Add(Z.HGroup(
                DialInt("Every Nth", g.spawnEvery, 1, 12,
                    "Which of the parent's dots this generator appears on: every Nth surviving one.",
                    v => g.spawnEvery = v, 1, 120f),
                Dial("Spawn chance", g.spawnChance, 0f, 100f,
                    "Chance that an eligible parent dot spawns this generator at all.",
                    v => g.spawnChance = v, 100f, 0, 175f)));

            generatorHost.Add(DialInt("Instance limit", g.maxInstances, 1, 250,
                "Hard ceiling on how many copies of this generator are evaluated, whatever the two dials above allow.",
                v => g.maxInstances = v, 80));
        }

        /// One fixed set of choices, drawn the same way everywhere: a short set is a joined segmented control,
        /// a longer one wraps as mini-radios. The same rule `ZuiReflect.EnumControl` applies to every enum it
        /// draws, so a choice built by hand and the same choice drawn by reflection cannot look like two
        /// different kinds of control in one window. (A PICKER over a growing list — the selector reference —
        /// deliberately stays a MiniRadio at every length; it must not change shape as names are added.)
        static VisualElement Choice(int index, string[] labels, string tooltip, Action<int> onChanged)
            => labels != null && labels.Length <= 3
                ? Z.Segmented(index, labels, tooltip, onChanged)
                : Z.MiniRadio(index, labels, tooltip, onChanged, wrap: true);

        static readonly string[] ShapeLabels = { "Rectangle", "Ellipse", "Diamond" };
        static readonly string[] AreaBasisLabels = { "Parent area", "Placement cell" };
        static readonly string[] AreaBasisParentOnly = { "Parent area" };

        /// Refill every section that follows the selection. Bodies only — the flow itself never moves, so the
        /// pane the user is working in stays where it was.
        void RefillSelectionSections()
        {
            RebuildGenerator();
            RebuildPlacement();
            RebuildSelectors();
            RebuildMutators();
            RebuildDrawers();
            MarkDirty();
        }

        void RefreshTreeNames()
        {
            if (doc == null) return;
            // Only the name label changes, so the row is relabelled rather than rebuilt: rebuilding it under a
            // pointer that is still typing would take the focus away mid-word.
            RebuildTree();
        }

        /// Called from the preview's repaint with the freshly evaluated result, so the readouts can never
        /// disagree with the picture beside them.
        void RefreshReadouts(DotGenResult res)
        {
            if (res == null) return;

            if (frameReadout != null)
                frameReadout.text = res.totalVisibleDots + " dots · " + res.totalAreas + " areas";

            if (doc == null) return;
            var tree = doc.Tree();
            for (int i = 0; i < treeMeta.Count; i++)
            {
                var (id, meta, count) = treeMeta[i];
                var g = doc.Find(id);
                if (g == null || meta == null) continue;

                var gd = res.For(g);
                var placement = g.ActivePlacement;
                string placementName = placement?.Meta?.DisplayName ?? "No placement";
                int dots = gd?.finalDots.Count ?? 0;
                int drawers = g.drawers?.Count ?? 0;
                meta.text = placementName + " · " + dots + " dots" + (drawers > 0 ? " · " + drawers + " drawer" + (drawers == 1 ? "" : "s") : "");

                if (count != null)
                {
                    int kids = tree.ChildrenOf(g).Count;
                    count.text = kids > 0 ? kids + " ▾" : "";
                }
            }
        }
    }
}
