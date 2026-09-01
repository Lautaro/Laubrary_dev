// ShaperMockWindow is a disposable UI blueprint for the FUTURE real Shaper editor. It owns only in-memory
// mock data — never ShaperNode / ShaperFillDef / any other committed Shaper production runtime type, and
// it cannot even name one: this file lives in its own assembly (ShaperMock.Editor), in its own namespace
// outside the Laubrary.Shaper tree, referencing only Laubrary.Zui.
//
// T-0130 built the first vertical slice: Canvas + Layers, a single Primitive layer's Shape + Fill
// (Solid/Gradient), a breadcrumb bar, a live pixel preview, Undo wired from the first control.
//
// T-0131 (this task) is the coordinator scope from SHAPER-UI-VISION-AND-DESIGN.md: Bag nesting with real
// breadcrumb push/pop, the remaining five fill kinds + palette-quantise, a Border stage, a document-level
// Light rig + per-node lighting response, extrusion/bevel, a Swarm card, an Effects list (Composite-only)
// and a simulated cache-state strip — plus a Composite node kind so the absence rule (§B4/§B5) has
// something to demonstrate against. Canvas/Light Rig/Layers stay OUTSIDE the node-scoped `nodeBody`
// container, so breadcrumb drilling never refolds or resizes them (§J2, the stable-workspace rule).
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaperMock.Editor
{
    public sealed class ShaperMockWindow : ZuiWindow
    {
        [MenuItem("Laubrary/Shaper Mock (Prototype)")]
        public static void Open()
        {
            var w = GetWindow<ShaperMockWindow>("Shaper Mock");
            w.minSize = new Vector2(820f, 520f);
        }

        // Not serialized on purpose: the mock is disposable, so a domain reload legitimately resets it —
        // EnsureDocument() is what keeps that from being a null-reference instead of a fresh seeded doc.
        ShaperMockDocument document;
        int selectedLayer;
        int currentFrame;

        // The breadcrumb DRILL PATH (§B3) — path[0] is always the selected layer's root node; path[i>0]
        // is a Bag member drilled into. View state, not model state (§B3's own rule) — EnsurePathValid()
        // repairs it after an Undo/Redo that restructures the tree out from under it.
        readonly List<ShaperMockNode> path = new List<ShaperMockNode>();

        VisualElement layerListHost;
        VisualElement nodeBody;         // the CURRENT node's own card stack (§B2) — rebuilt on drill/select
        ZuiBreadcrumb breadcrumb;
        ShaperMockPreviewStage stage;
        CacheStrip cacheStrip;
        Label cacheSummary;

        // ── section-toggle-bar roster (Pyre parity, T-0133) — every top-level and node-scoped ZuiSection
        // this window builds, held as fields so ZuiSectionToggleBar can bulk show/hide them. The bar itself
        // is thrown away and rebuilt (RefreshToggleBar) any time the roster's section INSTANCES change —
        // a full window Rebuild() (Canvas/Light Rig/Layers get new instances) or a RebuildNodeBody()-only
        // pass (Shape/Fill/Border/Swarm/Effects do) — because a stale bar would hold dead references.
        // A null entry (Fill/Border/Effects absent per §B4's absence rule) is skipped harmlessly by the bar
        // itself, exactly like Pyre's own null-safe TagsSection entry.
        ZuiSection canvasSection, lightRigSection, layersSection;
        ZuiSection shapeSection, fillSection, borderSection, swarmSection, effectsSection;
        VisualElement toggleBarHost;

        // Rebuild() recreates the left ScrollView, so its offset has to be carried across by hand or the
        // pane snaps to the top under the control the user was just using (mirrors ChunksMockWindow).
        ScrollView leftPane;
        Vector2 carriedScroll;

        protected override void BuildUI(VisualElement root)
        {
            EnsureDocument();
            root.style.minHeight = 0f;

            toggleBarHost = new VisualElement();
            root.Add(toggleBarHost);

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 320f;
            left.style.minHeight = 0f;
            BuildLeft(left.contentContainer);
            leftPane = left;
            RestoreScroll(left);

            var right = new VisualElement();
            right.style.minWidth = 260f;
            right.style.minHeight = 0f;
            BuildRight(right);

            // 560, not the more common 400, as the INITIAL divider position (still user-draggable and
            // persisted, same TwoPaneSplitView convention as every other Laubrary tool) — Shaper is meant
            // to be operated as a dense workbench with the window given real width, and the packed-row
            // dial groups above only pay off once the left pane is wide enough to show more than a pair
            // per row on first open, rather than requiring a user to discover the drag-to-widen affordance.
            // Kept at 560 rather than pushed wider to clear every packed row: the window's OWN measured
            // minimum (820x520, §J4.1) reserves 260 for the right pane, so the left pane cannot grow much
            // past 560 at that minimum without the two panes' own minimums conflicting — Canvas's remaining
            // few pixels of overflow (§Canvas below) are closed by trimming its own control widths instead.
            root.Add(Z.Split("shaper.mock.split.v1", 560f, left, right));
            RefreshToggleBar();
        }

        // ── section-toggle-bar (Pyre parity, T-0133) ────────────────────────────────────────────────────
        // Mirrors PyreWindow.BuildSectionToggleBar exactly: nothing Shaper-specific beyond listing which
        // sections exist right now. Placed at the very top of root, above the split, same as Pyre.
        void RefreshToggleBar()
        {
            if (toggleBarHost == null) return;
            toggleBarHost.Clear();
            toggleBarHost.Add(new ZuiSectionToggleBar("ShaperMock",
                ("Canvas", canvasSection),
                ("Light Rig", lightRigSection),
                ("Layers", layersSection),
                ("Shape", shapeSection),
                ("Fill", fillSection),
                ("Border", borderSection),
                ("Swarm", swarmSection),
                ("Effects", effectsSection)));
        }

        protected override void OnBeforeRebuild()
        {
            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
            leftPane = null;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= BakeTick;
        }

        void RestoreScroll(ScrollView view)
        {
            if (carriedScroll == Vector2.zero) return;
            Vector2 wanted = carriedScroll;
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                view.contentContainer.UnregisterCallback(once);
                view.scrollOffset = wanted;
            };
            view.contentContainer.RegisterCallback(once);
        }

        void EnsureDocument()
        {
            if (document == null)
            {
                document = ShaperMockDocument.CreateSeeded();
                StartSimulatedBake();
            }
            if (selectedLayer < 0 || selectedLayer >= document.layers.Count) selectedLayer = 0;
            EnsurePathValid();
        }

        ShaperMockLayer Selected => document != null && selectedLayer >= 0 && selectedLayer < document.layers.Count
            ? document.layers[selectedLayer] : null;

        ShaperMockNode Current => path.Count > 0 ? path[path.Count - 1] : null;

        /// Repairs `path` after Undo/Redo (or a layer switch) restructures the tree out from under it:
        /// truncates at the first entry that no longer belongs to its claimed parent Bag. Cheap at mock
        /// scale, and the only defence a plain object-reference path has against a snapshot restore.
        void EnsurePathValid()
        {
            var layer = Selected;
            if (layer == null || layer.root == null) { path.Clear(); return; }
            if (path.Count == 0 || path[0] != layer.root)
            {
                path.Clear();
                path.Add(layer.root);
            }
            for (int i = 1; i < path.Count; i++)
            {
                var parent = path[i - 1];
                if (parent.kind != ShaperMockNodeKind.Bag || parent.bagMembers == null
                    || !parent.bagMembers.Contains(path[i]))
                {
                    path.RemoveRange(i, path.Count - i);
                    break;
                }
            }
        }

        // ── left pane: Canvas, Light Rig, Layers, then the CURRENT node's own cards ─────────────────
        // Canvas/Light Rig/Layers are built directly into `root` — never inside `nodeBody` — so breadcrumb
        // drilling (which only ever calls RebuildNodeBody) can never refold or resize them (§J2).

        void BuildLeft(VisualElement root)
        {
            BuildCanvas(root);
            BuildLightRig(root);
            BuildLayers(root);

            nodeBody = new VisualElement();
            RebuildNodeBody();
            root.Add(nodeBody);
        }

        void BuildCanvas(VisualElement root)
        {
            var box = canvasSection = Z.Section("Canvas", "The output resolution, frame count and seed.", "shaper.mock.canvas",
                icon: "frame-corners");
            // One packed row, not two stacked ones — all four are short scalars, none is a spatial pair, so
            // they share a row per the UI Guide's row-packing rule and simply wrap to a second line at the
            // mock's minimum window width instead of needing two hardcoded rows. Sliders sized 125 (not
            // Dial()'s usual 140) specifically so all four measure comfortably inside the section body at
            // the coded minimum window size — at 140 each, the four needed 553px against a measured 547px
            // section width, six pixels short, so Seed always wrapped alone onto an otherwise-empty second
            // line (caught live by the project owner, T-0136). 125 clears it with real margin, not a
            // hairline fit.
            box.Add(Z.HGroup(
                Z.MicroSlider("Width", document.canvas.width, 8f, 256f, "Canvas width in pixels.",
                    v => Change(() => document.canvas.width = Mathf.RoundToInt(v)), 125f, decimals: 0),
                Z.MicroSlider("Height", document.canvas.height, 8f, 256f, "Canvas height in pixels.",
                    v => Change(() => document.canvas.height = Mathf.RoundToInt(v)), 125f, decimals: 0),
                Z.MicroSlider("Frames", document.canvas.frameCount, 1f, 64f,
                    "How many frames the document bakes to. Changing this shows/hides the transport.",
                    v => SetFrameCount(Mathf.RoundToInt(v)), 125f, decimals: 0),
                Z.Field("Seed", "Random seed — every dial's randomness (once this mock has any) derives from it.",
                    Z.Int(document.canvas.seed, "Random seed.",
                        v => Change(() => document.canvas.seed = v), 70f))));
            root.Add(box);
        }

        void SetFrameCount(int v)
        {
            Undo.RecordObject(document, "Edit Shaper Mock");
            document.canvas.frameCount = Mathf.Max(1, v);
            EditorUtility.SetDirty(document);
            InvalidateCache();
            Rebuild();   // transport visibility depends on frameCount>1 — a structural change, not a dial tweak
        }

        void BuildLayers(VisualElement root)
        {
            var box = layersSection = Z.Section("Layers",
                "The document's layers. Click a layer to edit its Shape/Fill below; drag the grip to reorder.",
                "shaper.mock.layers", icon: "stack");
            layerListHost = new VisualElement();
            box.Add(layerListHost);
            RebuildLayerList();
            box.Add(Z.Button("+ Add layer", "Append a new Primitive layer to the document (undoable).",
                AddLayer).W(110f));
            root.Add(box);
        }

        void RebuildLayerList()
        {
            if (layerListHost == null) return;
            layerListHost.Clear();
            for (int i = 0; i < document.layers.Count; i++)
                layerListHost.Add(BuildLayerRow(layerListHost, i));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int li)
        {
            var layer = document.layers[li];
            bool sel = li == selectedLayer;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var l = document.layers[from];
                    document.layers.RemoveAt(from);
                    document.layers.Insert(to, l);
                });
                selectedLayer = to;
                RebuildLayerList();
                RebuildNodeBody();
                RefreshBreadcrumb();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this layer in the preview.", layer.enabled, v =>
            {
                Change(() => layer.enabled = v);
                RebuildLayerList();
            }));

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () => SelectLayer(li)).W(24f));

            var name = Z.TextInput(layer.name ?? "", "This layer's name — rename it right here.", v =>
            {
                Undo.RecordObject(document, "Rename layer");
                layer.name = v;
                EditorUtility.SetDirty(document);
                if (sel) RefreshBreadcrumb();
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");   // the rulebook's name-field stretch exception
            name.RegisterCallback<PointerDownEvent>(_ => { if (selectedLayer != li) SelectLayer(li); });
            row.Add(name);

            var remove = Z.Button("×", "Remove this layer (undoable).", () => RemoveLayer(li)).W(20f);
            remove.SetEnabled(document.layers.Count > 1);   // always keep at least one layer to edit
            row.Add(remove);
            return row;
        }

        void SelectLayer(int li)
        {
            if (li == selectedLayer) return;
            selectedLayer = li;
            path.Clear();
            if (Selected?.root != null) path.Add(Selected.root);
            RebuildLayerList();
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        void AddLayer()
        {
            Undo.RecordObject(document, "Add layer");
            document.layers.Add(new ShaperMockLayer { name = "Layer " + (document.layers.Count + 1) });
            EditorUtility.SetDirty(document);
            selectedLayer = document.layers.Count - 1;
            path.Clear();
            path.Add(Selected.root);
            InvalidateCache();
            RebuildLayerList();
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        void RemoveLayer(int li)
        {
            if (document.layers.Count <= 1) return;
            Undo.RecordObject(document, "Remove layer");
            document.layers.RemoveAt(li);
            EditorUtility.SetDirty(document);
            if (selectedLayer >= document.layers.Count) selectedLayer = document.layers.Count - 1;
            path.Clear();
            if (Selected?.root != null) path.Add(Selected.root);
            InvalidateCache();
            RebuildLayerList();
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        // ── document-level Light Rig (§D2) — lives OUTSIDE nodeBody: never refolds on breadcrumb drill ──

        void BuildLightRig(VisualElement root)
        {
            var box = lightRigSection = Z.Section("Light Rig",
                "The document's single light rig (one per document, shared by every layer).",
                "shaper.mock.lightrig", icon: "sun");
            var rig = document.lightRig;

            box.Add(Z.HGroup(
                Val("Ambient", "Overall ambient light level.", rig.ambientIntensity, 0f, 2f),
                Z.Field("Ambient colour", "The ambient light's colour.",
                    Z.Color(rig.ambientColour, "The ambient light's colour.",
                        c => Change(() => rig.ambientColour = c), 90f))));
            box.Add(Z.Help(
                "Silhouette and Solids will not produce identical results from identical lights. What can and "
                + "should be made identical is the shading law — a difference between the two here is a "
                + "documented limit, not a bug.", HelpBoxMessageType.Info));

            var listHost = new VisualElement();
            box.Add(listHost);
            void RebuildLights()
            {
                listHost.Clear();
                for (int i = 0; i < rig.lights.Count; i++)
                    listHost.Add(BuildLightRow(rig, listHost, i, RebuildLights));
            }
            RebuildLights();

            box.Add(Z.Button("+ Add light", "Add a light to the document's rig.", () =>
            {
                Change(() => rig.lights.Add(ShaperMockLight.CreateDefault("Light " + (rig.lights.Count + 1))));
                RebuildLights();
            }).W(90f));

            root.Add(box);
        }

        VisualElement BuildLightRow(ShaperMockLightRig rig, VisualElement listHost, int i, Action rebuild)
        {
            var light = rig.lights[i];
            var box = Z.Box(null, null);
            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var l = rig.lights[from];
                    rig.lights.RemoveAt(from);
                    rig.lights.Insert(to, l);
                });
                rebuild();
            });
            header.Add(grip);

            var enableToggle = Z.Toggle("", "Enable or disable this light.", light.enabled,
                v => Change(() => light.enabled = v));
            header.Add(enableToggle);

            var swatch = Z.Color(light.colour, "This light's colour.", c => Change(() => light.colour = c), 40f);
            header.Add(swatch);

            var name = Z.TextInput(light.name ?? "", "This light's name.", v =>
            {
                Undo.RecordObject(document, "Rename light");
                light.name = v;
                EditorUtility.SetDirty(document);
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");
            header.Add(name);

            var removeBtn = Z.Button("×", "Remove this light (undoable).", () =>
            {
                Change(() => rig.lights.RemoveAt(i));
                rebuild();
            }).W(20f);
            removeBtn.SetEnabled(rig.lights.Count > 1);
            header.Add(removeBtn);
            box.Add(header);

            // Two packed rows of four instead of four stacked rows of two — none of these eight are a
            // spatial pair on their own (Pos X/Y/Z are three independent 1D dials here, not a Vector2/3
            // pad), so they all share rows and wrap down at narrow widths rather than always sitting one
            // pair per line regardless of how much width the window actually has.
            // ONE continuous HGroup for all eight dials, not two separate groups of four: splitting them
            // gave each group its own independent wrap boundary, so whichever dial didn't fit on a group's
            // first line became the LAST item in that group with nothing after it to share the line with —
            // an orphaned single control with a wasted gap beside it (caught live by the project owner,
            // T-0136: exactly the anti-pattern the earlier packing fix was supposed to remove, just
            // relocated). Merged into one flow, an item that doesn't fit falls onto the SAME wrapped line as
            // whatever comes after it in this group, not alone — Pitch lands beside Pos X, Pos Y instead of
            // by itself; Specular only ever ends up alone if the whole group's count happens to leave
            // exactly one item on the last line, which a single 8-item group makes far less likely than two
            // independent 4-item ones.
            var body = new VisualElement();
            body.Add(Z.HGroup(
                Val("Intensity", "This light's brightness.", light.intensity, 0f, 4f),
                Val("Range", "How far this light reaches.", light.range, 0.1f, 20f),
                Val("Yaw", "Horizontal direction, in degrees.", light.yaw, -180f, 180f, cyclic: true, decimals: 0),
                Val("Pitch", "Vertical direction, in degrees.", light.pitch, -90f, 90f, decimals: 0),
                Val("Pos X", "Light position X.", light.posX, -5f, 5f),
                Val("Pos Y", "Light position Y.", light.posY, -5f, 5f),
                Val("Pos Z", "Light position Z.", light.posZ, -5f, 5f),
                Val("Specular", "This light's specular contribution.", light.specular, 0f, 1f)));
            box.Add(body);

            ZuiFoldCard.Wire(light, header, body, enableToggle, removeBtn, swatch, name);
            return box;
        }

        // ── the CURRENT node's own card stack: Shape, [Fill], [Border], Swarm, [Lighting], [Effects] ──
        // (§B4/§B5 absence rule) — the only container drilling rebuilds (§J2).

        void RebuildNodeBody()
        {
            if (nodeBody == null) return;
            EnsurePathValid();
            nodeBody.Clear();
            var node = Current;
            if (node == null) return;

            bool isComposite = node.kind == ShaperMockNodeKind.Composite;
            bool subtractMember = path.Count > 1 && node.combineMode == ShaperMockCombineMode.Subtract;

            // Reset the conditional entries every rebuild — a null roster entry is skipped harmlessly by
            // ZuiSectionToggleBar (§C4/absence rule's own UI consequence, same pattern as the sections
            // themselves), so a node kind that doesn't build Fill/Border/Effects just leaves them null here.
            fillSection = null;
            borderSection = null;
            effectsSection = null;

            nodeBody.Add(BuildShapeSection(node, subtractMember));

            // Fill: absent entirely on a Composite (§B4/§B5) and on a Subtract bag member (§B4/FC-3.3) —
            // the Shape section above already carries the Subtract explanation, so the Fill section for
            // that node just doesn't render at all rather than showing a disabled stand-in.
            if (!isComposite && !subtractMember)
                nodeBody.Add(BuildFillSection(node));

            // Border: absent by default at every level, and entirely absent (not greyed) on a Composite.
            if (!isComposite)
                nodeBody.Add(BuildBorderSection(node));

            // Swarm: universal — every node kind gets this card, starting collapsed.
            nodeBody.Add(BuildSwarmSection(node));

            nodeBody.Add(BuildLightResponseBox(node));

            // Effects: only a Composite-sourced node gets this pipeline (§B4) — absent, not disabled, for
            // Primitive/Bag.
            if (isComposite)
                nodeBody.Add(BuildEffectsSection(node));

            RefreshToggleBar();
        }

        VisualElement BuildShapeSection(ShaperMockNode node, bool subtractMember)
        {
            var box = shapeSection = Z.Section("Shape",
                "This node's own geometry. The dial set shown depends entirely on its kind.",
                "shaper.mock.shape", icon: "shapes");

            var kindPicker = Z.MiniRadio((int)node.kind, new[] { "Primitive", "Bag", "Composite" },
                "What kind of node this is — a Primitive shape, a Bag combining children, or a Composite "
                + "(baked-raster) generator. Switching kind rebuilds every card below (§B4's absence rule).",
                v =>
                {
                    Change(() => node.kind = (ShaperMockNodeKind)v);
                    RebuildNodeBody();
                });
            box.Add(Z.Field("Node kind", "What kind of node this is.", kindPicker));

            switch (node.kind)
            {
                case ShaperMockNodeKind.Primitive:
                    box.Add(BuildPrimitiveBody(node));
                    box.Add(BuildExtrusionBlock(node));
                    break;

                case ShaperMockNodeKind.Bag:
                    box.Add(BuildBagMembersBody(node));
                    break;

                case ShaperMockNodeKind.Composite:
                    // The generator picker lives in the SECTION HEADER (Pyre parity — PyreWindow's own
                    // shape-FORM picker: "a header context menu... instead of in-body radio rows —
                    // reclaiming vertical space", ShowShapeMenu), not an in-body radio grid. Caught live by
                    // the project owner (T-0137): "look at how Pyre does it... choosing generators will be
                    // the same." Wired here, once per fresh Section instance (RebuildNodeBody always builds
                    // a brand new one, so this never double-registers).
                    SetupGeneratorHeaderMenu(box, node);
                    box.Add(BuildCompositeBody(node));
                    box.Add(BuildExtrusionBlock(node));
                    break;
            }

            if (subtractMember)
                box.Add(Z.Help("Subtract members carve; they don't paint — put a fill on the bag or a "
                    + "sibling instead.", HelpBoxMessageType.Info));

            return box;
        }

        VisualElement BuildPrimitiveBody(ShaperMockNode node)
        {
            var kindBody = new VisualElement();
            void RebuildKindBody()
            {
                kindBody.Clear();
                switch (node.shapeKind)
                {
                    case ShaperMockShapeKind.Disc:
                        kindBody.Add(Dial("Radius", "The disc's radius.", node.discRadius, 0.02f, 1f,
                            v => node.discRadius = v));
                        break;

                    case ShaperMockShapeKind.Ngon:
                        // One packed row of four instead of two stacked rows of two.
                        kindBody.Add(Z.HGroup(
                            Dial("Sides", "How many sides the polygon has.", node.ngonSides, 3f, 16f,
                                v => node.ngonSides = Mathf.RoundToInt(v), decimals: 0),
                            Dial("Radius", "The polygon's circumradius.", node.ngonRadius, 0.02f, 1f,
                                v => node.ngonRadius = v),
                            Dial("Rotation", "The polygon's rotation, in degrees.", node.ngonRotation, 0f, 360f,
                                v => node.ngonRotation = v, decimals: 0),
                            Dial("Corner radius", "Rounds each corner by this fraction.", node.ngonCornerRadius,
                                0f, 1f, v => node.ngonCornerRadius = v)));
                        break;

                    case ShaperMockShapeKind.Star:
                        // One packed row of five instead of two stacked pairs plus an orphaned single row.
                        kindBody.Add(Z.HGroup(
                            Dial("Arms", "How many points the star has.", node.starArms, 3f, 12f,
                                v => node.starArms = Mathf.RoundToInt(v), decimals: 0),
                            Dial("Radius", "The star's outer radius.", node.starRadius, 0.02f, 1f,
                                v => node.starRadius = v),
                            Dial("Length", "How far the arms reach, relative to the outer radius.",
                                node.starLength, 0.05f, 1f, v => node.starLength = v),
                            Dial("Base width", "How wide each arm's base is.", node.starBaseWidth, 0.02f, 1f,
                                v => node.starBaseWidth = v),
                            Dial("Skew", "Twists the arms, in degrees.", node.starSkew, -180f, 180f,
                                v => node.starSkew = v, decimals: 0)));
                        break;
                }
            }
            RebuildKindBody();

            var picker = Z.MiniRadio((int)node.shapeKind, new[] { "Disc", "N-gon", "Star" },
                "Which primitive this node generates. Switching kind rebuilds the dial set below.",
                v => Change(() =>
                {
                    node.shapeKind = (ShaperMockShapeKind)v;
                    RebuildKindBody();
                }));
            var wrap = new VisualElement();
            wrap.Add(Z.Field("Kind", "Which primitive this node generates.", picker));
            wrap.Add(kindBody);
            return wrap;
        }

        // A caret button in the "Shape" section header opens this menu (also right-click anywhere on the
        // header) — mirrors PyreWindow.ShowShapeMenu exactly, including its column grouping. Pyre groups
        // its forms 3D / 2D / Special; here the two columns are the REAL classification T-0112 measured
        // (PyreCompositeCatalog's own doc comment) — Palette-indifferent vs. Veil-multiplied — a sourced
        // fact, not an arbitrary split invented to mimic the shape of Pyre's menu.
        void SetupGeneratorHeaderMenu(ZuiSection box, ShaperMockNode node)
        {
            string CurrentName() => ShaperMockCompositeCatalog.All[
                Mathf.Clamp(node.compositeGeneratorIndex, 0, ShaperMockCompositeCatalog.All.Length - 1)].DisplayName;
            box.SetHeaderSuffix(() => " — " + CurrentName());
            box.SetHeaderMenu("caret-down", "Choose the composite generator (or right-click the title).",
                anchor => ShowGeneratorMenu(node, anchor));
        }

        void ShowGeneratorMenu(ShaperMockNode node, VisualElement anchor)
        {
            var menu = Z.Menu(anchor).Width(360f);
            menu.Custom((body, close) =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(GeneratorColumn(node, "Palette-indifferent", true, close));
                row.Add(GeneratorColumn(node, "Veil-multiplied", false, close));
                body.Add(row);
            });
            menu.Show();
        }

        VisualElement GeneratorColumn(ShaperMockNode node, string title, bool paletteIndifferent, Action close)
        {
            var col = new VisualElement { style = { flexDirection = FlexDirection.Column, marginRight = 12f, minWidth = 150f } };
            var head = new Label(title) { tooltip = paletteIndifferent
                ? "This generator's outline is completely indifferent to the palette."
                : "The palette's transparency is multiplied into an edge rule this generator computed on its own." };
            head.AddToClassList("zui-menu__section");
            col.Add(head);
            var all = ShaperMockCompositeCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].PaletteIndifferent != paletteIndifferent) continue;
                int idx = i;
                bool selected = node.compositeGeneratorIndex == idx;
                var item = new VisualElement { tooltip = all[idx].ReasonNote };
                item.AddToClassList("zui-menu__item");
                var check = new Label(selected ? "✓" : "") { pickingMode = PickingMode.Ignore };
                check.AddToClassList("zui-menu__check");
                item.Add(check);
                item.Add(new Label(all[idx].DisplayName) { pickingMode = PickingMode.Ignore });
                item.AddManipulator(new Clickable(() =>
                {
                    Change(() =>
                    {
                        node.compositeGeneratorIndex = idx;
                        node.generatorParams = all[idx].NewParams();
                        node.compositeReasonNote = all[idx].ReasonNote;
                    });
                    RebuildNodeBody();
                    close?.Invoke();
                }));
                col.Add(item);
            }
            return col;
        }

        VisualElement BuildCompositeBody(ShaperMockNode node)
        {
            var body = new VisualElement();
            var entry = ShaperMockCompositeCatalog.All[
                Mathf.Clamp(node.compositeGeneratorIndex, 0, ShaperMockCompositeCatalog.All.Length - 1)];

            body.Add(Z.HGroup(
                Z.Field("Reason",
                    "Why this node is still a Composite rather than split into Primitives — a structural "
                    + "compliance fact the audit checks, not authoring data.",
                    Z.Text(node.compositeReason.ToString(), ZuiText.Body, node.compositeReason.ToString())),
                Z.Field("Palette", "Whether this generator's own outline depends on the palette at all — a "
                    + "sourced fact from T-0112's classification, not an authored choice.",
                    Z.Text(entry.PaletteIndifferent ? "Indifferent" : "Veil-multiplied", ZuiText.Body,
                        entry.PaletteIndifferent
                            ? "This generator's outline is completely indifferent to the palette."
                            : "The palette's transparency is multiplied into this generator's own edge rule."))));

            body.Add(Z.Field("Reason note", "The one free-text field this card has — a declaration of why.",
                Z.TextInput(node.compositeReasonNote ?? "", "Why is this still a composite?",
                    v => { Undo.RecordObject(document, "Edit Shaper Mock"); node.compositeReasonNote = v;
                        EditorUtility.SetDirty(document); }, 220f)));

            // The selected generator's OWN authored dials (T-0137) — reflected with zero per-generator UI
            // code, the same ZuiReflect pattern the effect list already uses, proving "picking a generator
            // shows that generator's own fields" is a real, demonstrated capability, not an empty promise.
            if (node.generatorParams == null) node.generatorParams = entry.NewParams();
            var paramsBox = Z.BoxKeyed(entry.DisplayName + " parameters",
                "This generator's own authored dials — representative stand-ins for the real Pyre form's "
                + "fields, hosted unmodified per T-0112's composite escape hatch.",
                "shaper.mock.genparams:" + node.GetHashCode());
            ZuiReflect.BuildFields(paramsBox, node.generatorParams, new ZuiReflect.Options
            {
                OnBeforeChange = () => Undo.RecordObject(document, "Edit Shaper Mock generator"),
                OnChanged = () => { EditorUtility.SetDirty(document); InvalidateCache(); RefreshPreview(); },
            });
            body.Add(paramsBox);

            return body;
        }

        VisualElement BuildExtrusionBlock(ShaperMockNode node)
        {
            var host = new VisualElement();
            void RebuildBlock()
            {
                host.Clear();
                bool flat = node.extrudeDepth.mode == ZUIValue.Mode.Static
                    && Mathf.Approximately(node.extrudeDepth.staticValue, 0f);
                if (flat)
                {
                    host.Add(Z.Button("+ Extrude", "Give this shape depth, bevel and taper — currently flat.",
                        () =>
                        {
                            Change(() => node.extrudeDepth.staticValue = 0.2f);
                            RebuildBlock();
                        }).W(80f));
                    return;
                }

                var box = Z.BoxKeyed("Extrusion",
                    "Depth, bevel and taper — already envelope-ready in the real engine, so every row here "
                    + "is a Z.Value.", "shaper.mock.extrude:" + node.GetHashCode());
                // One packed row of six rather than three stacked rows of two.
                box.Add(Z.HGroup(
                    Val("Depth", "How far this shape extrudes.", node.extrudeDepth, 0f, 1f),
                    Val("Angle", "The extrusion's lean angle, in degrees.", node.extrudeAngle, -90f, 90f, decimals: 0),
                    Val("Curve", "Bulges or pinches the extrusion's profile.", node.extrudeCurve, -1f, 1f),
                    Val("Taper", "Narrows the far end of the extrusion.", node.extrudeTaper, -1f, 1f),
                    Val("Bevel amt", "How much the extrusion's edge bevels.", node.bevelAmount, 0f, 1f),
                    Val("Bevel steps", "How many facets the bevel uses.", node.bevelSteps, 0f, 8f, decimals: 0)));
                box.Add(Z.Button("Flatten", "Reset depth to 0 (collapses this block).", () =>
                {
                    Change(() => node.extrudeDepth.staticValue = 0f);
                    RebuildBlock();
                }).W(70f));
                host.Add(box);
            }
            RebuildBlock();
            return host;
        }

        // ── Bag members (§B2, §B4) ───────────────────────────────────────────────────────────────────

        VisualElement BuildBagMembersBody(ShaperMockNode bag)
        {
            var host = new VisualElement();
            var listHost = new VisualElement();
            host.Add(listHost);

            void RebuildMembers()
            {
                listHost.Clear();
                for (int i = 0; i < bag.bagMembers.Count; i++)
                    listHost.Add(BuildMemberRow(bag, listHost, i, RebuildMembers));
            }
            RebuildMembers();

            host.Add(Z.Button("+ Add member", "Append a new Primitive member to this bag.", () =>
            {
                Change(() => bag.bagMembers.Add(
                    ShaperMockNode.NewBagMember("Member " + (bag.bagMembers.Count + 1), ShaperMockCombineMode.Add)));
                RebuildMembers();
            }).W(110f));
            return host;
        }

        VisualElement BuildMemberRow(ShaperMockNode bag, VisualElement listHost, int i, Action rebuildList)
        {
            var member = bag.bagMembers[i];
            var row = new VisualElement();
            row.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this member.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var m = bag.bagMembers[from];
                    bag.bagMembers.RemoveAt(from);
                    bag.bagMembers.Insert(to, m);
                });
                rebuildList();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Enable or disable this member.", member.enabled, v =>
            {
                Change(() => member.enabled = v);
            }));

            var name = Z.TextInput(member.name ?? "", "This member's name.", v =>
            {
                Undo.RecordObject(document, "Rename member");
                member.name = v;
                EditorUtility.SetDirty(document);
                RefreshBreadcrumb();
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");
            row.Add(name);

            // Combine mode appears only with ≥2 members (§B2) — the first member always establishes the
            // base shape, so a combine picker on it would offer a choice with no visible effect.
            if (i > 0)
            {
                var combine = Z.MiniRadio((int)member.combineMode, new[] { "Add", "Sub", "Int", "Blend" },
                    "How this member combines with the accumulated shape above it.",
                    v => Change(() =>
                    {
                        member.combineMode = (ShaperMockCombineMode)v;
                        // A Subtract member may not own a fill (§B4/FC-3.3) — enforced structurally, not
                        // just hidden in the UI.
                        if (member.combineMode == ShaperMockCombineMode.Subtract) member.fill = null;
                    }));
                row.Add(combine);
            }

            var drill = Z.Button("▶", "Drill into this member to edit its own Shape/Fill.",
                () => DrillInto(member)).W(24f);
            row.Add(drill);

            var remove = Z.Button("×", "Remove this member (undoable).", () =>
            {
                Change(() => bag.bagMembers.RemoveAt(i));
                rebuildList();
            }).W(20f);
            row.Add(remove);
            return row;
        }

        void DrillInto(ShaperMockNode member)
        {
            path.Add(member);
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        // ── Fill (§C) ────────────────────────────────────────────────────────────────────────────────

        VisualElement BuildFillSection(ShaperMockNode node)
        {
            var box = fillSection = Z.Section("Fill", "How this node is painted.", "shaper.mock.fill", icon: "palette");

            if (node.fill == null)
            {
                box.Add(Z.Help($"Painted by {NearestFillAncestorName()}'s fill.", HelpBoxMessageType.None));
                box.Add(Z.Button("+ Add fill", "Give this node its own fill instead of being painted by an ancestor.",
                    () =>
                    {
                        Change(() => node.fill = new ShaperMockFill());
                        RebuildNodeBody();
                    }).W(90f));
                return box;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            var control = new ShaperMockFillControl("Fill", node.fill, "How this node is painted — "
                + "right-click to choose the kind.")
            {
                OnBeforeMutate = () => Undo.RecordObject(document, "Edit Shaper Mock"),
                OnChanged = () => { EditorUtility.SetDirty(document); InvalidateCache(); RefreshPreview(); },
            };
            control.style.flexGrow = 1f;
            row.Add(control);

            // A node one level below the root may give up its own fill — root layer nodes always own one.
            if (path.Count > 1)
            {
                var removeFill = Z.Button("✕ own fill",
                    "Remove this node's own fill — it will be painted by the nearest ancestor's fill instead.",
                    () =>
                    {
                        Change(() => node.fill = null);
                        RebuildNodeBody();
                    }).W(80f);
                row.Add(removeFill);
            }
            box.Add(row);
            return box;
        }

        string NearestFillAncestorName()
        {
            for (int i = path.Count - 2; i >= 0; i--)
                if (path[i].fill != null) return i == 0 ? Selected.name : path[i].name;
            return Selected != null ? Selected.name : "the layer";
        }

        // ── Border (§D1) ────────────────────────────────────────────────────────────────────────────

        VisualElement BuildBorderSection(ShaperMockNode node)
        {
            var box = borderSection = Z.Section("Border", "An outward strip around this node's own silhouette.",
                "shaper.mock.border", icon: "square");

            // Same header-checkbox treatment as Swarm (Pyre parity, T-0136): present/absent IS this whole
            // section's one job, so it belongs on the header rather than a body-level "+ Add"/"Remove" pair
            // that costs a row only while the section happens to be open.
            box.SetHeaderToggle(node.border != null,
                "Off = no border. On = an outward strip around this node's own silhouette, with its own "
                + "width and fill.",
                v =>
                {
                    Change(() => node.border = v ? new ShaperMockBorder() : null);
                    RebuildNodeBody();
                });

            if (node.border == null) return box;

            var b = node.border;
            box.Add(Val("Width", "How far the strip extends outward, in shape units. Already envelope-ready.",
                b.width, 0f, 0.3f));
            box.Add(Z.Toggle("Joins coverage",
                "Include the outward strip in this node's own published coverage (default on).",
                b.joinsCoverage, v => Change(() => b.joinsCoverage = v)));

            var stripFillControl = new ShaperMockFillControl("Strip fill", b.stripFill,
                "The fill that paints the border strip — the strip is a region; a fill paints it, same as "
                + "any other fill.")
            {
                OnBeforeMutate = () => Undo.RecordObject(document, "Edit Shaper Mock"),
                OnChanged = () => { EditorUtility.SetDirty(document); InvalidateCache(); RefreshPreview(); },
            };
            box.Add(stripFillControl);

            return box;
        }

        // ── Swarm (§E) ──────────────────────────────────────────────────────────────────────────────

        VisualElement BuildSwarmSection(ShaperMockNode node)
        {
            var box = swarmSection = Z.Section("Swarm",
                "Scatter many instances of this node, jittered per-instance. Present on every node kind.",
                "shaper.mock.swarm", icon: "sparkle");
            var s = node.swarm;

            // A HEADER checkbox (Pyre parity — PyreWindow.RebuildSwarm's own swarmSection.SetHeaderToggle),
            // not a body-level "+ Enable swarm" button: the enable/disable IS this whole section's one job,
            // so it belongs on the header everyone already sees whether the section is folded or not,
            // rather than costing its own full row inside the body only when open (caught live by the
            // project owner, T-0136 — "look at how Pyre does it").
            box.SetHeaderToggle(s.enabled,
                "Off = a single instance (this node's own Shape alone). On = Count instances scattered with "
                + "per-instance jitter.",
                v => { Change(() => s.enabled = v); RebuildNodeBody(); });

            if (!s.enabled) return box;

            bool native = NativeSwarmAvailable(node);
            int cap = native ? 24 : ShaperMockSwarm.HardCap;
            bool capped = s.count > cap;
            int resolved = capped ? cap : s.count;
            string countTip = capped
                ? $"How many instances to scatter — authored {s.count}, but this node's swarm path holds a "
                  + $"per-instance state budget capped at {resolved}, so the effective count is {resolved}."
                : "How many instances to scatter.";

            // Count/Rot jitter/Scale jitter/Lifetime stagger are four independent scalars, so they share one
            // packed row; Pos jitter is a spatial pair and stays on its own row per the UI Guide's explicit
            // X/Y-pair exclusion from row-packing (a Z.Pad, never packed beside 1D fields).
            box.Add(Z.HGroup(
                Z.MicroSlider(capped ? $"Count ({resolved} eff.)" : "Count", s.count, 1f, ShaperMockSwarm.HardCap,
                    countTip, v => Change(() => s.count = Mathf.RoundToInt(v)), 160f, decimals: 0),
                Z.MicroSlider("Rot jitter", s.rotationJitterDegrees, 0f, 180f,
                    "Random rotation spread per instance, in degrees.",
                    v => Change(() => s.rotationJitterDegrees = v), 130f, decimals: 0),
                Z.MicroSlider("Scale jitter", s.scaleJitter, 0f, 1f, "Random scale spread per instance.",
                    v => Change(() => s.scaleJitter = v), 130f),
                Z.MicroSlider("Lifetime stagger", s.lifetimeStagger, 0f, 1f,
                    "Randomly staggers each instance's life phase so a burst doesn't animate in lockstep.",
                    v => Change(() => s.lifetimeStagger = v), 150f)));
            box.Add(Z.Field("Pos jitter", "How much each instance's position randomly varies.",
                Z.Pad(s.positionJitter, new Rect(-1f, -1f, 2f, 2f),
                    "How much each instance's position randomly varies.",
                    v => Change(() => s.positionJitter = v), 40f)));

            string badge = native
                ? "Interact — active (Native: " + SourceName(node) + ")"
                : "Interact — has no effect here (Generic wrapper; this source has no native swarm path)";
            box.Add(Z.Toggle(badge,
                native
                    ? "Instances influence each other under this source's native swarm path (e.g. cross-"
                      + "instance heat diffusion)."
                    : "Shown, never hidden, because it changes what the control means: this toggle stays "
                      + "interactive so you can set intent now, but it currently does nothing — this source "
                      + "has no native swarm implementation, only the generic O(N) wrapper.",
                s.interact, v => Change(() => s.interact = v)));

            return box;
        }

        static bool NativeSwarmAvailable(ShaperMockNode node) => node.kind == ShaperMockNodeKind.Composite;
        static string SourceName(ShaperMockNode node) => node.kind == ShaperMockNodeKind.Composite
            ? ShaperMockCompositeCatalog.All[Mathf.Clamp(node.compositeGeneratorIndex, 0,
                ShaperMockCompositeCatalog.All.Length - 1)].DisplayName
            : "n/a";

        // ── Lighting response (§D2) ─────────────────────────────────────────────────────────────────

        VisualElement BuildLightResponseBox(ShaperMockNode node)
        {
            var r = node.lightResponse;
            var box = Z.BoxKeyed("Lighting response", "How this node reacts to the document's light rig.",
                "shaper.mock.lightresp:" + node.GetHashCode());
            box.Add(Z.HGroup(
                Z.Toggle("Receive lighting", "Whether the light rig affects this node at all.",
                    r.receiveLighting, v => Change(() => r.receiveLighting = v)),
                Z.Toggle("Cast shadows", "Whether this node casts shadows onto other layers.",
                    r.castShadows, v => Change(() => r.castShadows = v)),
                Z.Toggle("Receive shadows", "Whether this node receives shadows from other layers.",
                    r.receiveShadows, v => Change(() => r.receiveShadows = v))));
            // One packed row of five instead of three stacked rows of two.
            box.Add(Z.HGroup(
                Val("Intensity ×", "Scales the light rig's effect on this node.", r.intensityScale, 0f, 3f),
                Val("Rim strength", "How strong the rim-light response is.", r.rimStrength, 0f, 2f),
                Val("Specular", "How strong the specular response is.", r.specular, 0f, 1f),
                Val("Spec power", "How tight the specular highlight is.", r.specularPower, 1f, 128f),
                Val("Rim power", "How tight the rim-light falloff is.", r.rimPower, 0.5f, 8f),
                Z.Field("Spec tint", "Tints the specular highlight.",
                    Z.Color(r.specularTint, "Tints the specular highlight.",
                        c => Change(() => r.specularTint = c), 90f))));
            return box;
        }

        // ── Effects (§F) — Composite-only (§B4) ────────────────────────────────────────────────────

        VisualElement BuildEffectsSection(ShaperMockNode node)
        {
            var box = effectsSection = Z.Section("Effects",
                "A universal-effects pipeline — only available on Composite (baked-raster) nodes.",
                "shaper.mock.effects", icon: "sliders-horizontal");
            var listHost = new VisualElement();
            box.Add(listHost);

            void Rebuild()
            {
                listHost.Clear();
                for (int i = 0; i < node.effects.Count; i++)
                    listHost.Add(BuildEffectBlock(node, listHost, i, Rebuild));
            }
            Rebuild();

            Button addBtn = null;
            addBtn = Z.Button("+ Add effect", "Add a pre- or post-composite effect to this node.",
                () => ShowAddEffectMenu(addBtn, node, Rebuild));
            box.Add(addBtn);
            return box;
        }

        VisualElement BuildEffectBlock(ShaperMockNode node, VisualElement listHost, int index, Action rebuild)
        {
            var e = node.effects[index];
            var box = Z.Box(null, null);
            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body,
                "Drag to reorder — an effect's position is its apply order within its stage.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var m = node.effects[from];
                    node.effects.RemoveAt(from);
                    node.effects.Insert(to, m);
                });
                rebuild();
            });
            header.Add(grip);

            var enableToggle = Z.Toggle("", "Enable or disable this effect.", e.enabled, v =>
            {
                Change(() => e.enabled = v);
                rebuild();
            });
            header.Add(enableToggle);

            var stageTag = Z.Text(e.Stage == ShaperMockEffectStage.Pre ? "PRE" : "POST", ZuiText.Body,
                e.Stage == ShaperMockEffectStage.Pre
                    ? "Runs before compositing."
                    : "Runs after compositing, over the whole finished frame.");
            stageTag.style.width = 34f;
            stageTag.style.unityFontStyleAndWeight = FontStyle.Bold;
            stageTag.style.opacity = 0.7f;
            header.Add(stageTag);

            header.Add(Z.Text(e.DisplayName, ZuiText.Body, e.DisplayName + " effect."));
            header.Add(Z.Flexible());
            var removeBtn = Z.Button("×", "Remove this effect (undoable).", () =>
            {
                int at = node.effects.IndexOf(e);
                if (at >= 0) { Change(() => node.effects.RemoveAt(at)); rebuild(); }
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            VisualElement body = null;
            if (e.enabled)
            {
                body = new VisualElement();
                ZuiReflect.BuildFields(body, e, new ZuiReflect.Options
                {
                    OnBeforeChange = () => Undo.RecordObject(document, "Edit Shaper Mock effect"),
                    OnChanged = () => { EditorUtility.SetDirty(document); InvalidateCache(); RefreshPreview(); },
                    OnStructureChanged = rebuild,
                    Skip = f => f.Name == "enabled",
                    TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {e.DisplayName} parameter.",
                });
                box.Add(body);
            }
            ZuiFoldCard.Wire(e, header, body, enableToggle, removeBtn);
            return box;
        }

        void ShowAddEffectMenu(VisualElement anchor, ShaperMockNode node, Action rebuild)
        {
            // .Search(): the real catalog this stands in for is 41 entries (T-0114) — comfortably more than
            // fits in one screenful, so this menu needs the filter + capped-scroll behaviour ZuiMenu now
            // offers (T-0135), not just a longer flat list.
            var menu = Z.Menu(anchor).Search("Search effects…");
            bool publishesSheets = ShaperMockCompositeCatalog.PublishesSheets(node.compositeGeneratorIndex);
            string lastGroup = null;
            foreach (var factory in ShaperMockEffectCatalog.Addable)
            {
                var sample = factory();
                string group = sample.Bucket.ToString();
                if (group != lastGroup) { menu.Section(group); lastGroup = group; }
                bool blocked = sample.NeedsSheetsUnmet(publishesSheets);
                var f = factory;
                menu.Item(sample.DisplayName,
                    blocked ? sample.UnmetReason : $"Add the {sample.DisplayName} effect ({sample.Stage}).",
                    () => { Change(() => node.effects.Add(f())); rebuild(); }, enabled: !blocked);
            }
            menu.Show();
        }

        // ── right pane: breadcrumb + preview + transport/cache-state strip (§G) ────────────────────

        void BuildRight(VisualElement root)
        {
            breadcrumb = Z.Breadcrumb(new List<string>(), null);
            root.Add(breadcrumb);
            RefreshBreadcrumb();

            var previewSection = Z.Section("Preview",
                "A pixel-exact live render of the selected layer's shape and fill — not a Mirage-quality preview.",
                "shaper.mock.preview", icon: "eye");
            previewSection.style.flexGrow = 1f;
            previewSection.style.minHeight = 0f;
            previewSection.contentContainer.style.flexGrow = 1f;
            previewSection.contentContainer.style.minHeight = 0f;

            stage = new ShaperMockPreviewStage(document, () => Selected);
            stage.style.flexGrow = 1f;
            stage.style.minHeight = 200f;
            previewSection.Add(stage);

            // Transport + cache-state strip (§G2) — only when the document has more than one frame (§B1).
            if (document.canvas.frameCount > 1)
                previewSection.Add(BuildTransport());

            root.Add(previewSection);
        }

        VisualElement BuildTransport()
        {
            var host = new VisualElement();
            int max = Mathf.Max(0, document.canvas.frameCount - 1);
            currentFrame = Mathf.Clamp(currentFrame, 0, max);

            var scrubber = Z.SliderInt(currentFrame, 0, max, "Scrub the frame.", v =>
            {
                currentFrame = v;
                RefreshPreview();
            }, 220f);
            host.Add(Z.Field("Frame", "Scrub the frame.", scrubber));

            cacheStrip = new CacheStrip(document);
            cacheStrip.style.height = 10f;
            cacheStrip.style.marginTop = 2f;
            host.Add(cacheStrip);

            cacheSummary = Z.Text("", ZuiText.Body, "How many of this document's frames are cache-baked "
                + "(simulated — no real bake runs in this mock).");
            host.Add(cacheSummary);
            RefreshCacheStrip();

            return host;
        }

        // Nothing to drill into yet at the layer-root scope beyond what a Bag adds — the breadcrumb reads
        // straight from `path`, so drilling in/out is a real push/pop, not a stand-in.
        void RefreshBreadcrumb()
        {
            if (breadcrumb == null) return;
            var layer = Selected;
            var segments = new List<string>();
            if (layer != null)
            {
                segments.Add(string.IsNullOrEmpty(layer.name) ? "(unnamed layer)" : layer.name);
                for (int i = 1; i < path.Count; i++)
                    segments.Add(string.IsNullOrEmpty(path[i].name) ? "(unnamed)" : path[i].name);
            }
            breadcrumb.SetPath(segments, i =>
            {
                if (i < 0 || i >= path.Count) return;
                path.RemoveRange(i + 1, path.Count - (i + 1));
                RebuildNodeBody();
                RefreshBreadcrumb();
            });
        }

        void RefreshPreview() => stage?.Refresh();

        // ── cache-state simulation (§G) — entirely mock; no real bake runs ─────────────────────────

        void StartSimulatedBake()
        {
            EditorApplication.update -= BakeTick;
            EditorApplication.update += BakeTick;
        }

        double lastBakeTick;
        void BakeTick()
        {
            if (document == null) { EditorApplication.update -= BakeTick; return; }
            int total = Mathf.Max(1, document.canvas.frameCount);
            if (document.cachedFrames.Count >= total) { EditorApplication.update -= BakeTick; return; }
            double now = EditorApplication.timeSinceStartup;
            if (now - lastBakeTick < 0.12) return;   // ~8 frames/sec — visibly progressive, not instant
            lastBakeTick = now;
            for (int i = 0; i < total; i++)
            {
                if (document.cachedFrames.Contains(i)) continue;
                document.cachedFrames.Add(i);
                break;
            }
            RefreshCacheStrip();
        }

        void InvalidateCache()
        {
            if (document == null) return;
            document.cachedFrames.Clear();
            RefreshCacheStrip();
            StartSimulatedBake();
        }

        void RefreshCacheStrip()
        {
            cacheStrip?.Refresh();
            if (cacheSummary == null || document == null) return;
            int cached = document.cachedFrames.Count;
            int total = document.canvas.frameCount;
            cacheSummary.text = $"{cached} / {total} frames cached" + (cached < total ? "  (baking…)" : "");
        }

        /// A thin per-frame tick strip: filled = cached, hollow = not (§G2). No new ZUI control needed —
        /// a small `generateVisualContent`-painted strip, the same technique ZuiEnvelope's thumbs use.
        sealed class CacheStrip : VisualElement
        {
            readonly ShaperMockDocument _doc;
            public CacheStrip(ShaperMockDocument doc)
            {
                _doc = doc;
                tooltip = "One tick per frame — filled means that frame is cache-baked, hollow means it "
                    + "isn't yet (recomputed live when scrubbed to).";
                generateVisualContent += Paint;
            }

            public void Refresh() => MarkDirtyRepaint();

            void Paint(MeshGenerationContext mgc)
            {
                var view = contentRect;
                int n = Mathf.Max(1, _doc?.canvas.frameCount ?? 1);
                if (view.width < 2f || view.height < 2f) return;
                var p = mgc.painter2D;
                float tickW = view.width / n;
                for (int i = 0; i < n; i++)
                {
                    bool cached = _doc != null && _doc.cachedFrames.Contains(i);
                    var r = new Rect(view.x + i * tickW + 1f, view.y, Mathf.Max(1f, tickW - 2f), view.height);
                    p.BeginPath();
                    p.MoveTo(new Vector2(r.x, r.y));
                    p.LineTo(new Vector2(r.xMax, r.y));
                    p.LineTo(new Vector2(r.xMax, r.yMax));
                    p.LineTo(new Vector2(r.x, r.yMax));
                    p.ClosePath();
                    if (cached)
                    {
                        p.fillColor = new Color(0.4f, 0.85f, 0.5f, 1f);
                        p.Fill();
                    }
                    else
                    {
                        p.strokeColor = new Color(0.7f, 0.7f, 0.75f, 0.9f);
                        p.lineWidth = 1f;
                        p.Stroke();
                    }
                }
            }
        }

        // ── Undo/dirty wiring — the Val()/FillRow() pattern (design doc §J1), from the very first dial ──

        VisualElement Dial(string label, string tooltip, float value, float min, float max, Action<float> set,
            int decimals = -1)
            => Z.MicroSlider(label, value, min, max, tooltip, v => Change(() => set(v)), 140f, decimals: decimals);

        /// The ZUIValue analog of Dial — every envelope-ready row (border width, light-rig dials,
        /// extrusion/bevel) goes through this, mirroring Pyre's own `Val()` helper exactly
        /// (`PyreWindow.cs:2574-2586`): hidden curve timing/range/readout so a packed row stays compact,
        /// `grow:false` so two fit per HGroup.
        VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi, bool cyclic = false,
            int decimals = -1)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = 130f, grow = false, cyclic = cyclic, decimals = decimals,
                frameCount = document != null ? document.canvas.frameCount : 0,
            };
            return Z.Value(label, v, o, tooltip,
                () => { EditorUtility.SetDirty(document); InvalidateCache(); RefreshPreview(); },
                () => Undo.RecordObject(document, "Edit Shaper Mock"));
        }

        void Change(Action apply)
        {
            if (document != null) Undo.RecordObject(document, "Edit Shaper Mock");
            apply();
            if (document != null) { EditorUtility.SetDirty(document); InvalidateCache(); }
            RefreshPreview();
        }
    }
}
