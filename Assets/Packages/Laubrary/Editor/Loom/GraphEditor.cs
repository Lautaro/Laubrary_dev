using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Loom;
using Laubrary.GraphViewKit;
using GV = UnityEditor.Experimental.GraphView;
using LEdge = Laubrary.Loom.Edge;

namespace Laubrary.Loom.Editor
{
    // ── The shared Loom graph editor ──────────────────────────────────────────────────────────────────────
    // A single GraphView canvas that authors ANY IGraphAsset (Story's Screenplay, Daemon's Brain, …): drop nodes
    // from the asset's node-type palette, wire named output ports, set the entry, and the topology + positions
    // persist back to the asset. Node FIELD values edit in-canvas (reflection) or in the Inspector. Live traversal
    // is polled off any running IGraphRunnerViz. Everything is generic over INode / IGraphAsset — no per-tool code.

    // A base window; a tool subclasses it only to add its [MenuItem] and title (see Story/Daemon).
    public abstract class GraphWindowBase : EditorWindow
    {
        protected IGraphAsset _asset;
        LoomGraphView _view;
        Label _header;
        protected abstract string WindowTitle { get; }

        public void OpenAsset(IGraphAsset asset)
        {
            titleContent = new GUIContent(WindowTitle);
            Load(asset);
            Show();
        }

        void OnEnable()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("lau-graph");
            // Attach only semantic graph presentation: the full toolkit sheet would also restyle native fields.
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Packages/Laubrary/Zui/Toolkit/ZuiFoundationToolShell.uss")
                ?? AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.lautaro.arino.laubrary/Zui/Toolkit/ZuiFoundationToolShell.uss");
            if (sheet != null && !rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            var toolbar = new VisualElement();
            toolbar.AddToClassList("lau-graph__toolbar");
            _header = new Label("No graph loaded");
            _header.AddToClassList("lau-graph__header");
            toolbar.Add(_header);
            toolbar.Add(new Button(() => { _view?.SaveToAsset(); AssetDatabase.SaveAssets(); }) { text = "Save" });
            toolbar.Add(new Button(() => _view?.FrameAll()) { text = "Frame All" });

            var fadeLabel = new Label("Fade");
            fadeLabel.AddToClassList("lau-graph__fade-label");
            toolbar.Add(fadeLabel);
            var fadeSlider = new Slider(0.1f, 5f) { value = EditorPrefs.GetFloat("Laubrary.Loom.FadeSeconds", 1.5f) };
            fadeSlider.AddToClassList("lau-graph__fade-slider");
            var fadeValueLabel = new Label(fadeSlider.value.ToString("0.0") + "s");
            fadeValueLabel.AddToClassList("lau-graph__fade-readout");
            fadeSlider.RegisterValueChangedCallback(e =>
            {
                if (_view != null) _view.FadeSeconds = e.newValue;
                EditorPrefs.SetFloat("Laubrary.Loom.FadeSeconds", e.newValue);
                fadeValueLabel.text = e.newValue.ToString("0.0") + "s";
            });
            toolbar.Add(fadeSlider);
            toolbar.Add(fadeValueLabel);

            var hint = new Label("  Add nodes: right-click the canvas.  Edit field values in-node or the Inspector.");
            hint.AddToClassList("lau-graph__hint");
            toolbar.Add(hint);
            rootVisualElement.Add(toolbar);

            _view = new LoomGraphView();
            _view.AddToClassList("lau-graph__canvas");
            rootVisualElement.Add(_view);
            if (_asset != null) Load(_asset);

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _view?.SaveToAsset(); if (_view != null) rootVisualElement.Remove(_view);
        }

        // Ctrl+Z reverts the asset's serialized Pages/Edges, but the GraphView's VisualElements are a separate
        // in-memory tree that Unity's Undo system doesn't know to refresh — re-Populate so a reverted delete
        // (or any other undone edit) actually reappears on the canvas.
        void OnUndoRedo() { if (_asset != null) _view?.Populate(_asset); }

        void Load(IGraphAsset asset)
        {
            _asset = asset;
            if (_view == null) return;
            _view.Populate(asset);
            if (_header != null) _header.text = asset?.AssetObject != null ? WindowTitle + ": " + asset.AssetObject.name : "No graph loaded";
        }
    }

    public class LoomGraphView : GV.GraphView
    {
        IGraphAsset _asset;

        // How long (seconds) a just-vacated node/edge takes to fade from the bright "current" glow down to its
        // resting look, instead of switching off the instant the flow moves on. A toolbar slider in
        // GraphWindowBase edits this live and persists it via EditorPrefs.
        public float FadeSeconds = UnityEditor.EditorPrefs.GetFloat("Laubrary.Loom.FadeSeconds", 1.5f);

        public LoomGraphView()
        {
            SetupZoom(GV.ContentZoomer.DefaultMinScale, GV.ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new GV.ContentDragger());
            this.AddManipulator(new GV.SelectionDragger());
            this.AddManipulator(new GV.RectangleSelector());
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            graphViewChanged = OnGraphChanged;
            schedule.Execute(UpdateRuntimeViz).Every(120);
        }

        // ── live traversal viz (any IGraphRunnerViz in the scene) ─────────────────────────────────────────
        bool _vizOn;
        static List<IGraphRunnerViz> FindRunners()
        {
            var list = new List<IGraphRunnerViz>();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IGraphRunnerViz v) list.Add(v);
            return list;
        }

        void UpdateRuntimeViz()
        {
            if (!Application.isPlaying) { if (_vizOn) { ClearViz(); _vizOn = false; } return; }
            _vizOn = true;
            var runners = FindRunners();

            foreach (var n in nodes.ToList().OfType<LoomNodeView>())
            {
                bool cur = false, vis = false; float since = float.MaxValue;
                foreach (var r in runners)
                {
                    if (r.IsCurrent(n.Node.Id)) cur = true;
                    if (r.WasVisited(n.Node.Id)) vis = true;
                    float s = r.SecondsSinceActive(n.Node.Id);
                    if (s < since) since = s;
                }
                n.SetViz(cur, vis, since, FadeSeconds);
            }

            // A logical Story/Daemon edge may render as several segments (real → reroute → … → real) when it's
            // been bent around other nodes. Resolve each segment back to its real endpoints so the WHOLE chain
            // gets one consistent viz state, not just the segment that happens to touch a real node.
            var allEdges = edges.ToList();
            foreach (var e in allEdges)
            {
                var (fromId, port, toId) = ResolveLogicalEndpoints(e, allEdges);
                if (fromId == null || toId == null) { SetEdgeViz(e, false, false, float.MaxValue, FadeSeconds); continue; }

                bool cur = false, trav = false; float since = float.MaxValue;
                foreach (var r in runners)
                {
                    // Exclusive: only the specific edge a bookmark actually walked lights up as "current" — NOT
                    // every edge that happens to end at the active node (e.g. a loop's two incoming wires).
                    if (r.IsCurrentEdge(fromId, port, toId)) cur = true;
                    if (r.WasEdgeTraversed(fromId, port, toId)) trav = true;
                    float s = r.SecondsSinceEdgeActive(fromId, port, toId);
                    if (s < since) since = s;
                }
                SetEdgeViz(e, cur, trav, since, FadeSeconds);
            }
        }

        static (string from, string port, string to) ResolveLogicalEndpoints(GV.Edge seg, List<GV.Edge> all)
        {
            var back = seg;
            for (int guard = 0; back.output?.node is UniversalRerouteNode rOut && guard < 64; guard++)
            {
                var incoming = rOut.OtherSlotPort(back.output);
                var prev = all.FirstOrDefault(x => x.input == incoming);
                if (prev == null) break;
                back = prev;
            }
            var fromView = back.output?.node as LoomNodeView;
            var portName = back.output?.portName;

            var fwd = seg;
            for (int guard = 0; fwd.input?.node is UniversalRerouteNode rIn && guard < 64; guard++)
            {
                var outgoing = rIn.OtherSlotPort(fwd.input);
                var next = all.FirstOrDefault(x => x.output == outgoing);
                if (next == null) break;
                fwd = next;
            }
            var toView = fwd.input?.node as LoomNodeView;
            return (fromView?.Node.Id, portName, toView?.Node.Id);
        }

        void ClearViz()
        {
            foreach (var n in nodes.ToList().OfType<LoomNodeView>()) n.SetViz(false, false, float.MaxValue, FadeSeconds);
            foreach (var e in edges.ToList()) SetEdgeViz(e, false, false, float.MaxValue, FadeSeconds);
        }

        static readonly Color CurrentColor = new Color(1f, 0.85f, 0.15f);
        static readonly Color TraversedColor = new Color(0.30f, 0.85f, 0.45f);
        static readonly Color IdleColor = new Color(0.5f, 0.5f, 0.5f);

        // Bright while the wire is THE path a bookmark just walked; once it stops being current, its glow eases
        // back down to "traversed" (or idle, if never traversed) over `fadeSeconds` instead of switching off.
        static void SetEdgeViz(GV.Edge e, bool isCurrent, bool wasTraversed, float secondsSinceActive, float fadeSeconds)
        {
            if (e?.edgeControl == null) return;
            Color baseCol = wasTraversed ? TraversedColor : IdleColor;
            float baseW = wasTraversed ? 4f : 2f;
            float alpha = isCurrent ? 1f : (fadeSeconds > 0f ? Mathf.Clamp01(1f - secondsSinceActive / fadeSeconds) : 0f);
            var col = Color.Lerp(baseCol, CurrentColor, alpha);
            var w = Mathf.Lerp(baseW, 5f, alpha);
            e.edgeControl.inputColor = col; e.edgeControl.outputColor = col;
            e.edgeControl.edgeWidth = Mathf.RoundToInt(w);
        }

        public void Populate(IGraphAsset asset)
        {
            _asset = asset;
            DeleteElements(graphElements.ToList());
            if (asset == null) return;

            var map = new Dictionary<string, LoomNodeView>();
            foreach (var node in asset.Nodes)
            {
                if (node == null) continue;
                if (string.IsNullOrEmpty(node.Id)) node.Id = Guid.NewGuid().ToString("N");
                var nv = CreateNode(node);
                AddElement(nv);
                map[node.Id] = nv;
            }
            if (asset.Edges != null)
                foreach (var e in asset.Edges)
                {
                    if (!map.TryGetValue(e.From, out var from) || !map.TryGetValue(e.To, out var to)) continue;
                    var op = from.OutPort(e.Port); var ip = to.InPort;
                    if (op == null || ip == null) continue;

                    GV.Port cur = op;
                    if (e.Waypoints != null)
                        foreach (var wp in e.Waypoints)
                        {
                            var reroute = new UniversalRerouteNode();
                            reroute.SetPosition(new Rect(wp, UniversalRerouteNode.Size));
                            AddElement(reroute);
                            HookReroute(reroute);
                            var seg = cur.ConnectTo(reroute.SlotAActivePort);
                            AddElement(seg); AttachEdgeHandlers(seg);
                            reroute.RefreshRole();
                            cur = reroute.SlotBActivePort;
                        }
                    var last = cur.ConnectTo(ip);
                    AddElement(last); AttachEdgeHandlers(last);
                }
        }

        LoomNodeView CreateNode(INode node)
        {
            var nv = new LoomNodeView(node, _asset);
            nv.SetPosition(new Rect(node.GraphPos, new Vector2(220, 120)));
            return nv;
        }

        public override List<GV.Port> GetCompatiblePorts(GV.Port startPort, GV.NodeAdapter adapter)
        {
            var list = new List<GV.Port>();
            ports.ForEach(p =>
            {
                if (startPort == p || startPort.node == p.node || startPort.direction == p.direction) return;
                if (p.style.display == DisplayStyle.None) return;   // a reroute's currently locked-out sub-port
                list.Add(p);
            });
            return list;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (_asset != null)
            {
                Vector2 mouse = contentViewContainer.WorldToLocal(evt.localMousePosition);
                foreach (var t in TypeCache.GetTypesDerivedFrom(_asset.NodeBaseType))
                {
                    if (t.IsAbstract) continue;
                    var type = t;
                    evt.menu.AppendAction("Add ▸ " + Pretty(type.Name), _ => AddNode(type, mouse));
                }
                evt.menu.AppendSeparator();
                if (selection.OfType<LoomNodeView>().FirstOrDefault() is LoomNodeView sel)
                    evt.menu.AppendAction("Set as Entry", _ =>
                    {
                        if (_asset.AssetObject != null) Undo.RegisterCompleteObjectUndo(_asset.AssetObject, "Set Entry Node");
                        _asset.EntryId = sel.Node.Id; Dirty(); Populate(_asset);
                    });
            }
            base.BuildContextualMenu(evt);
        }

        void AddNode(Type type, Vector2 pos)
        {
            if (_asset.AssetObject != null) Undo.RegisterCompleteObjectUndo(_asset.AssetObject, "Add Node");
            var node = _asset.AddNode(type, Guid.NewGuid().ToString("N"), pos);
            if (_asset.IsEntryNode(node) && string.IsNullOrEmpty(_asset.EntryId)) _asset.EntryId = node.Id;
            AddElement(CreateNode(node));
            Dirty();
        }

        GV.GraphViewChange OnGraphChanged(GV.GraphViewChange change)
        {
            // A user-drawn edge arrives here via edgesToCreate; make sure it's in the view and gets the same
            // double-click-to-reroute handler as everything Populate() builds.
            if (change.edgesToCreate != null)
                foreach (var e in change.edgesToCreate)
                {
                    if (e.parent == null) AddElement(e);
                    AttachEdgeHandlers(e);
                }

            // Deleting a reroute waypoint must not sever the logical connection it was bending — splice the real
            // ports it sat between back together with a direct edge once Unity finishes tearing it down.
            if (change.elementsToRemove != null)
                foreach (var r in change.elementsToRemove.OfType<UniversalRerouteNode>().ToList())
                    ReconnectAround(r);

            // Any reroute's locked in/out role may need to react to a wire the user just dragged straight onto
            // one of its slots (not just ones created via the double-click-insert flow below).
            foreach (var r in nodes.ToList().OfType<UniversalRerouteNode>()) r.RefreshRole();

            EditorApplication.delayCall += () => { if (this != null && _asset != null) SaveToAsset(); };
            return change;
        }

        // Wires a freshly created reroute node's events back into this view: a slot's orientation flip rebuilds
        // its ports and hands back a replacement edge to add, and any internal topology change (role lock,
        // orientation flip) should persist like any other graph edit.
        void HookReroute(UniversalRerouteNode r)
        {
            r.EdgeReplaced += edge => { AddElement(edge); AttachEdgeHandlers(edge); };
            r.Changed += () => EditorApplication.delayCall += () => { if (this != null && _asset != null) SaveToAsset(); };
        }

        // Double-click a wire to drop a drag-able bend point on it (visual only — no graph logic). The standard
        // technique node editors (Shader Graph, Blueprints) use so a back-edge in a loop can arc around other
        // nodes instead of cutting a straight diagonal through the middle of the graph.
        void InsertReroute(GV.Edge edge, Vector2 localPos)
        {
            var outputPort = edge.output; var inputPort = edge.input;
            if (outputPort == null || inputPort == null) return;

            RemoveElement(edge);
            outputPort.Disconnect(edge); inputPort.Disconnect(edge);

            var reroute = new UniversalRerouteNode();
            reroute.SetPosition(new Rect(localPos - UniversalRerouteNode.Size / 2f, UniversalRerouteNode.Size));
            AddElement(reroute);
            HookReroute(reroute);

            var seg1 = outputPort.ConnectTo(reroute.SlotAActivePort);
            AddElement(seg1); AttachEdgeHandlers(seg1);
            reroute.RefreshRole();

            var seg2 = reroute.SlotBActivePort.ConnectTo(inputPort);
            AddElement(seg2); AttachEdgeHandlers(seg2);

            EditorApplication.delayCall += () => { if (this != null && _asset != null) SaveToAsset(); };
        }

        // NOTE: only handles a single reroute being removed at a time — deleting two chained waypoints in one
        // multi-select still works per-node but won't re-merge across both in the same pass. Rare enough (you'd
        // need several bends on one wire) not to be worth the extra bookkeeping right now.
        void ReconnectAround(UniversalRerouteNode r)
        {
            var all = edges.ToList();
            GV.Edge inEdge = null, outEdge = null;
            foreach (var p in r.AllPorts)
            {
                if (!p.connected) continue;
                var e = all.FirstOrDefault(x => x.input == p || x.output == p);
                if (e == null) continue;
                if (e.input == p) inEdge = e; else outEdge = e;
            }
            if (inEdge?.output == null || outEdge?.input == null) return;
            var realOut = inEdge.output; var realIn = outEdge.input;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                var merged = realOut.ConnectTo(realIn);
                AddElement(merged); AttachEdgeHandlers(merged);
            };
        }

        void AttachEdgeHandlers(GV.Edge e)
        {
            e.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2 && evt.button == 0)
                {
                    InsertReroute(e, contentViewContainer.WorldToLocal(evt.mousePosition));
                    evt.StopPropagation();
                }
            });
        }

        public void SaveToAsset()
        {
            if (_asset == null) return;
            // Register BEFORE mutating — this snapshots the asset's current (pre-edit) Pages/Edges as the Undo
            // baseline. Covers every structural change (delete, reroute insert/remove, drag) since they all
            // funnel through here via the delayCall in OnGraphChanged.
            if (_asset.AssetObject != null) Undo.RegisterCompleteObjectUndo(_asset.AssetObject, "Edit Graph");

            var views = nodes.ToList().OfType<LoomNodeView>().ToList();
            foreach (var n in views) n.Node.GraphPos = n.GetPosition().position;
            _asset.RebuildNodes(views.Select(n => n.Node).ToList());

            // Walk each chain forward from a real output port through any reroute waypoints to the real input
            // port it ultimately reaches, collapsing it back into one logical edge (with the bend positions).
            var allEdges = edges.ToList();
            var el = new List<LEdge>();
            foreach (var e in allEdges)
            {
                if (!(e.output?.node is LoomNodeView fromView)) continue;
                var portName = e.output.portName;

                var waypoints = new List<Vector2>();
                var seg = e;
                bool broken = false;
                for (int guard = 0; seg.input?.node is UniversalRerouteNode reroute; guard++)
                {
                    if (guard >= 64) { broken = true; break; }
                    waypoints.Add(reroute.GetPosition().position);
                    var outgoing = reroute.OtherSlotPort(seg.input);
                    var next = allEdges.FirstOrDefault(x => x.output == outgoing);
                    if (next == null) { broken = true; break; }
                    seg = next;
                }
                if (broken || !(seg.input?.node is LoomNodeView toView)) continue;

                el.Add(new LEdge(fromView.Node.Id, portName, toView.Node.Id)
                { Waypoints = waypoints.Count > 0 ? waypoints.ToArray() : null });
            }
            _asset.Edges = el;
            if (_asset.GetNodeUntyped(_asset.EntryId) == null) _asset.EntryId = _asset.ResolveEntry();
            Dirty();
        }

        void Dirty() { if (_asset?.AssetObject != null) EditorUtility.SetDirty(_asset.AssetObject); }

        public new void FrameAll() => schedule.Execute(() => base.FrameAll()).ExecuteLater(1);

        static string Pretty(string n)
        {
            if (n.EndsWith("Page")) n = n.Substring(0, n.Length - 4);
            else if (n.EndsWith("Node")) n = n.Substring(0, n.Length - 4);
            return n;
        }
    }

    // One node view = one INode. Input on the left (unless it's an entry node), one output per Node.Ports.
    public class LoomNodeView : GV.Node
    {
        public readonly INode Node;
        public GV.Port InPort { get; private set; }
        readonly Dictionary<string, GV.Port> _outs = new Dictionary<string, GV.Port>();
        readonly IGraphAsset _asset;

        public LoomNodeView(INode node, IGraphAsset asset)
        {
            Node = node; _asset = asset;
            AddToClassList("lau-graph__node");
            title = TitleText();

            var kind = new Label(node.GetType().Name);
            kind.AddToClassList("lau-graph__node-kind");
            mainContainer.Add(kind);

            if (!asset.IsEntryNode(node))
            {
                InPort = GV.Port.Create<GV.Edge>(GV.Orientation.Horizontal, GV.Direction.Input, GV.Port.Capacity.Multi, typeof(bool));
                InPort.portName = "in";
                inputContainer.Add(InPort);
            }
            var ports = node.Ports;
            if (ports != null)
                foreach (var pn in ports)
                {
                    var op = GV.Port.Create<GV.Edge>(GV.Orientation.Horizontal, GV.Direction.Output, GV.Port.Capacity.Multi, typeof(bool));
                    op.portName = pn; _outs[pn] = op; outputContainer.Add(op);
                }

            BuildFields();
            expanded = true; RefreshExpandedState(); RefreshPorts();
        }

        string TitleText()
        {
            string mark = (_asset != null && _asset.EntryId == Node.Id) ? "▶ " : "";
            return mark + Node.DisplayTitle;
        }

        // Must be called BEFORE the field mutation it guards — RegisterCompleteObjectUndo snapshots whatever
        // the asset currently looks like as the Undo baseline, so calling it after the value already changed
        // would bake the NEW value in as "the thing to undo back to."
        void Dirty()
        {
            if (_asset?.AssetObject == null) return;
            Undo.RegisterCompleteObjectUndo(_asset.AssetObject, "Edit " + Node.DisplayTitle);
            EditorUtility.SetDirty(_asset.AssetObject);
        }

        // Reflection-driven in-node editors for the node's public fields (scalars / bool / enum / List<T> of
        // [Serializable] elements). Everything else points to the Inspector (SerializeReference etc.).
        void BuildFields()
        {
            var section = new VisualElement();
            section.AddToClassList("lau-graph__fields");
            foreach (var fi in Node.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                section.Add(BuildField(fi, Node));
            extensionContainer.Add(section);
        }

        VisualElement BuildField(FieldInfo fi, object target, Action refresh = null)
        {
            var t = fi.FieldType; string name = fi.Name;
            if (t == typeof(string))
            {
                var dd = fi.GetCustomAttribute<GraphDropdownAttribute>();
                if (dd != null) return BuildDropdown(fi, target, dd, refresh);

                bool multi = fi.GetCustomAttribute<TextAreaAttribute>() != null;
                var f = new TextField(name) { value = (string)fi.GetValue(target) ?? "", multiline = multi };
                f.labelElement.AddToClassList("lau-graph__field-label");
                f.RegisterValueChangedCallback(e => { Dirty(); fi.SetValue(target, e.newValue); if (name == "Title" && target == (object)Node) title = TitleText(); });
                return f;
            }
            if (t == typeof(int)) { var f = new IntegerField(name) { value = (int)fi.GetValue(target) }; f.labelElement.AddToClassList("lau-graph__field-label"); f.RegisterValueChangedCallback(e => { Dirty(); fi.SetValue(target, e.newValue); }); return f; }
            if (t == typeof(float)) { var f = new FloatField(name) { value = (float)fi.GetValue(target) }; f.labelElement.AddToClassList("lau-graph__field-label"); f.RegisterValueChangedCallback(e => { Dirty(); fi.SetValue(target, e.newValue); }); return f; }
            if (t == typeof(bool)) { var f = new Toggle(name) { value = (bool)fi.GetValue(target) }; f.labelElement.AddToClassList("lau-graph__field-label"); f.RegisterValueChangedCallback(e => { Dirty(); fi.SetValue(target, e.newValue); }); return f; }
            if (t.IsEnum) { var f = new EnumField(name, (Enum)fi.GetValue(target)); f.labelElement.AddToClassList("lau-graph__field-label"); f.RegisterValueChangedCallback(e => { Dirty(); fi.SetValue(target, e.newValue); }); return f; }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return BuildList(fi, target);
            var hint = new Label(name + ": (edit in Inspector)");
            hint.AddToClassList("lau-graph__field-hint");
            return hint;
        }

        // A dropdown for a [GraphDropdown]-annotated string field. Options come from an instance method on the
        // target; the current value stays selectable even if it's no longer a valid choice (so stale data shows).
        VisualElement BuildDropdown(FieldInfo fi, object target, GraphDropdownAttribute dd, Action refresh)
        {
            var options = ResolveChoices(target, dd.ChoicesMethod);
            string cur = (string)fi.GetValue(target) ?? "";
            if (!string.IsNullOrEmpty(cur) && !options.Contains(cur)) options.Insert(0, cur);
            if (options.Count == 0) options.Add("");

            var f = new DropdownField(fi.Name, options, Mathf.Max(0, options.IndexOf(cur)));
            f.labelElement.AddToClassList("lau-graph__field-label");
            f.RegisterValueChangedCallback(e =>
            {
                Dirty();
                fi.SetValue(target, e.newValue);
                refresh?.Invoke();   // dependent dropdowns (e.g. a field list that depends on the chosen rule) rebuild
            });
            return f;
        }

        static List<string> ResolveChoices(object target, string method)
        {
            try
            {
                var mi = target?.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
                if (mi != null && typeof(System.Collections.IEnumerable).IsAssignableFrom(mi.ReturnType))
                    if (mi.Invoke(target, null) is System.Collections.IEnumerable res)
                        return res.Cast<object>().Select(o => o?.ToString() ?? "").ToList();
            }
            catch { /* a bad provider shouldn't break the node */ }
            return new List<string>();
        }

        VisualElement BuildList(FieldInfo fi, object target)
        {
            var elemType = fi.FieldType.GetGenericArguments()[0];
            var box = new VisualElement();
            box.AddToClassList("lau-graph__list");
            Action rebuild = null;
            rebuild = () =>
            {
                box.Clear();
                var head = new VisualElement();
                head.AddToClassList("lau-graph__list-header");
                var name = new Label(fi.Name);
                name.AddToClassList("lau-graph__list-name");
                head.Add(name);
                head.Add(new Button(() => { Dirty(); EnsureList(fi, target).Add(NewElem(elemType)); rebuild(); }) { text = "+ Add" });
                box.Add(head);
                var list = fi.GetValue(target) as System.Collections.IList;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        int idx = i; var item = list[idx];
                        var row = new VisualElement();
                        row.AddToClassList("lau-graph__list-entry");
                        var rowHead = new VisualElement();
                        rowHead.AddToClassList("lau-graph__entry-header");
                        var ordinal = new Label("#" + idx);
                        ordinal.AddToClassList("lau-graph__entry-ordinal");
                        rowHead.Add(ordinal);
                        rowHead.Add(new Button(() => { Dirty(); EnsureList(fi, target).RemoveAt(idx); rebuild(); }) { text = "✕" });
                        row.Add(rowHead);
                        if (item != null)
                            foreach (var efi in elemType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                                row.Add(BuildField(efi, item, rebuild));   // rebuild = refresh dependent dropdowns
                        box.Add(row);
                    }
            };
            rebuild();
            return box;
        }

        System.Collections.IList EnsureList(FieldInfo fi, object target)
        {
            var list = fi.GetValue(target) as System.Collections.IList;
            if (list == null) { list = (System.Collections.IList)Activator.CreateInstance(fi.FieldType); fi.SetValue(target, list); }
            return list;
        }

        static object NewElem(Type elemType) => elemType == typeof(string) ? "" : Activator.CreateInstance(elemType);

        public GV.Port OutPort(string name)
        {
            if (name != null && _outs.TryGetValue(name, out var p)) return p;
            return _outs.Values.FirstOrDefault();
        }

        static readonly Color VizCurrentColor = new Color(1f, 0.85f, 0.15f);
        static readonly Color VizVisitedColor = new Color(0.30f, 0.80f, 0.45f);

        // Bright while THIS node is current; once it stops being current, the border eases back down to
        // "visited" (or off, if never visited) over `fadeSeconds` instead of switching off abruptly.
        public void SetViz(bool isCurrent, bool wasVisited, float secondsSinceActive, float fadeSeconds)
        {
            Color baseCol = wasVisited ? VizVisitedColor : Color.clear;
            float baseW = wasVisited ? 2f : 0f;
            float alpha = isCurrent ? 1f : (fadeSeconds > 0f ? Mathf.Clamp01(1f - secondsSinceActive / fadeSeconds) : 0f);
            var border = Color.Lerp(baseCol, VizCurrentColor, alpha);
            var w = Mathf.Lerp(baseW, 4f, alpha);
            style.borderTopColor = border; style.borderBottomColor = border; style.borderLeftColor = border; style.borderRightColor = border;
            style.borderTopWidth = w; style.borderBottomWidth = w; style.borderLeftWidth = w; style.borderRightWidth = w;
            titleContainer.style.backgroundColor = alpha > 0.02f
                ? new StyleColor(new Color(0.55f, 0.45f, 0.05f, 0.55f * alpha))
                : new StyleColor(StyleKeyword.Null);
        }
    }
}
