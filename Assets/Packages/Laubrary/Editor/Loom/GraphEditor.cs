using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Loom;
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
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 6, paddingTop = 4, paddingBottom = 4 } };
            _header = new Label("No graph loaded") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12, alignSelf = Align.Center } };
            toolbar.Add(_header);
            toolbar.Add(new Button(() => { _view?.SaveToAsset(); AssetDatabase.SaveAssets(); }) { text = "Save" });
            toolbar.Add(new Button(() => _view?.FrameAll()) { text = "Frame All" });
            toolbar.Add(new Label("  Add nodes: right-click the canvas.  Edit field values in-node or the Inspector.")
            { style = { alignSelf = Align.Center, opacity = 0.7f } });
            rootVisualElement.Add(toolbar);

            _view = new LoomGraphView { style = { flexGrow = 1 } };
            rootVisualElement.Add(_view);
            if (_asset != null) Load(_asset);
        }

        void OnDisable() { _view?.SaveToAsset(); if (_view != null) rootVisualElement.Remove(_view); }

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
                bool cur = false, vis = false;
                foreach (var r in runners) { if (r.IsCurrent(n.Node.Id)) { cur = true; break; } if (r.WasVisited(n.Node.Id)) vis = true; }
                n.SetViz(cur ? 2 : vis ? 1 : 0);
            }
            foreach (var e in edges.ToList())
            {
                var o = e.output?.node as LoomNodeView;
                var i = e.input?.node as LoomNodeView;
                bool trav = o != null && i != null && runners.Any(r => r.WasEdgeTraversed(o.Node.Id, e.output.portName, i.Node.Id));
                SetEdgeViz(e, trav);
            }
        }

        void ClearViz()
        {
            foreach (var n in nodes.ToList().OfType<LoomNodeView>()) n.SetViz(0);
            foreach (var e in edges.ToList()) SetEdgeViz(e, false);
        }

        static void SetEdgeViz(GV.Edge e, bool traversed)
        {
            if (e?.edgeControl == null) return;
            var col = traversed ? new Color(0.30f, 0.85f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
            e.edgeControl.inputColor = col; e.edgeControl.outputColor = col;
            e.edgeControl.edgeWidth = traversed ? 4 : 2;
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
                    AddElement(op.ConnectTo(ip));
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
            ports.ForEach(p => { if (startPort != p && startPort.node != p.node && startPort.direction != p.direction) list.Add(p); });
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
                    evt.menu.AppendAction("Set as Entry", _ => { _asset.EntryId = sel.Node.Id; Dirty(); Populate(_asset); });
            }
            base.BuildContextualMenu(evt);
        }

        void AddNode(Type type, Vector2 pos)
        {
            var node = _asset.AddNode(type, Guid.NewGuid().ToString("N"), pos);
            if (_asset.IsEntryNode(node) && string.IsNullOrEmpty(_asset.EntryId)) _asset.EntryId = node.Id;
            AddElement(CreateNode(node));
            Dirty();
        }

        GV.GraphViewChange OnGraphChanged(GV.GraphViewChange change)
        {
            EditorApplication.delayCall += () => { if (this != null && _asset != null) SaveToAsset(); };
            return change;
        }

        public void SaveToAsset()
        {
            if (_asset == null) return;
            var views = nodes.ToList().OfType<LoomNodeView>().ToList();
            foreach (var n in views) n.Node.GraphPos = n.GetPosition().position;
            _asset.RebuildNodes(views.Select(n => n.Node).ToList());

            var el = new List<LEdge>();
            foreach (var e in edges.ToList())
            {
                var o = e.output?.node as LoomNodeView;
                var i = e.input?.node as LoomNodeView;
                if (o == null || i == null) continue;
                el.Add(new LEdge(o.Node.Id, e.output.portName, i.Node.Id));
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
            style.minWidth = 240;
            title = TitleText();

            mainContainer.Add(new Label(node.GetType().Name) { style = { opacity = 0.6f, marginLeft = 6, marginBottom = 2, fontSize = 10 } });

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

        void Dirty() { if (_asset?.AssetObject != null) EditorUtility.SetDirty(_asset.AssetObject); }

        // Reflection-driven in-node editors for the node's public fields (scalars / bool / enum / List<T> of
        // [Serializable] elements). Everything else points to the Inspector (SerializeReference etc.).
        void BuildFields()
        {
            var section = new VisualElement { style = { paddingLeft = 6, paddingRight = 6, paddingTop = 2, paddingBottom = 4 } };
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
                f.labelElement.style.minWidth = 70;
                f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); if (name == "Title" && target == (object)Node) title = TitleText(); Dirty(); });
                return f;
            }
            if (t == typeof(int)) { var f = new IntegerField(name) { value = (int)fi.GetValue(target) }; f.labelElement.style.minWidth = 70; f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); Dirty(); }); return f; }
            if (t == typeof(float)) { var f = new FloatField(name) { value = (float)fi.GetValue(target) }; f.labelElement.style.minWidth = 70; f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); Dirty(); }); return f; }
            if (t == typeof(bool)) { var f = new Toggle(name) { value = (bool)fi.GetValue(target) }; f.labelElement.style.minWidth = 70; f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); Dirty(); }); return f; }
            if (t.IsEnum) { var f = new EnumField(name, (Enum)fi.GetValue(target)); f.labelElement.style.minWidth = 70; f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); Dirty(); }); return f; }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return BuildList(fi, target);
            return new Label(name + ": (edit in Inspector)") { style = { opacity = 0.5f, fontSize = 10, marginTop = 2, whiteSpace = WhiteSpace.Normal } };
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
            f.labelElement.style.minWidth = 70;
            f.RegisterValueChangedCallback(e =>
            {
                fi.SetValue(target, e.newValue);
                Dirty();
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
            var box = new VisualElement { style = { marginTop = 3, paddingLeft = 4, borderLeftWidth = 2, borderLeftColor = new Color(1f, 1f, 1f, 0.15f) } };
            Action rebuild = null;
            rebuild = () =>
            {
                box.Clear();
                var head = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                head.Add(new Label(fi.Name) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11, flexGrow = 1 } });
                head.Add(new Button(() => { EnsureList(fi, target).Add(NewElem(elemType)); Dirty(); rebuild(); }) { text = "+ Add" });
                box.Add(head);
                var list = fi.GetValue(target) as System.Collections.IList;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        int idx = i; var item = list[idx];
                        var row = new VisualElement { style = { marginBottom = 3, paddingLeft = 3, paddingBottom = 2, borderLeftWidth = 1, borderLeftColor = new Color(1f, 1f, 1f, 0.1f) } };
                        var rowHead = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                        rowHead.Add(new Label("#" + idx) { style = { flexGrow = 1, fontSize = 10, opacity = 0.6f } });
                        rowHead.Add(new Button(() => { EnsureList(fi, target).RemoveAt(idx); Dirty(); rebuild(); }) { text = "✕" });
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

        public void SetViz(int state)
        {
            Color border; float w;
            if (state == 2) { border = new Color(1f, 0.85f, 0.15f); w = 4f; }
            else if (state == 1) { border = new Color(0.30f, 0.80f, 0.45f); w = 2f; }
            else { border = Color.clear; w = 0f; }
            style.borderTopColor = border; style.borderBottomColor = border; style.borderLeftColor = border; style.borderRightColor = border;
            style.borderTopWidth = w; style.borderBottomWidth = w; style.borderLeftWidth = w; style.borderRightWidth = w;
            titleContainer.style.backgroundColor = state == 2 ? new StyleColor(new Color(0.55f, 0.45f, 0.05f, 0.55f)) : new StyleColor(StyleKeyword.Null);
        }
    }
}
