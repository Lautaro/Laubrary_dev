using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Story;
using GV = UnityEditor.Experimental.GraphView;

namespace Laubrary.Story.Editor
{
    // A node-canvas editor for Screenplay assets: drop in Pages, wire their output ports to other pages, set
    // the entry, and the whole topology + node positions persist back to the asset. Page FIELD values are
    // edited in the normal Inspector (the [SerializeReference] Pages list) — this window owns the graph shape.
    public class StoryGraphWindow : EditorWindow
    {
        Screenplay _screenplay;
        StoryGraphView _view;
        Label _header;

        public static void Open(Screenplay sp)
        {
            var w = GetWindow<StoryGraphWindow>();
            w.titleContent = new GUIContent("Story Graph");
            w.Load(sp);
            w.Show();
        }

        [MenuItem("TrueEye/Story Graph")]
        public static void OpenEmpty()
        {
            var w = GetWindow<StoryGraphWindow>();
            w.titleContent = new GUIContent("Story Graph");
            if (Selection.activeObject is Screenplay sp) w.Load(sp);
            w.Show();
        }

        void OnEnable()
        {
            rootVisualElement.Clear();

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 6, paddingTop = 4, paddingBottom = 4 } };
            _header = new Label("No Screenplay loaded") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12, alignSelf = Align.Center } };
            toolbar.Add(_header);
            var save = new Button(() => { _view?.SaveToAsset(); AssetDatabase.SaveAssets(); }) { text = "Save" };
            toolbar.Add(save);
            var frame = new Button(() => _view?.FrameAll()) { text = "Frame All" };
            toolbar.Add(frame);
            toolbar.Add(new Label("  Add nodes: right-click the canvas.  Edit field values in the Inspector.") { style = { alignSelf = Align.Center, opacity = 0.7f } });
            rootVisualElement.Add(toolbar);

            _view = new StoryGraphView { style = { flexGrow = 1 } };
            rootVisualElement.Add(_view);

            if (_screenplay != null) Load(_screenplay);
        }

        void OnDisable() { _view?.SaveToAsset(); if (_view != null) rootVisualElement.Remove(_view); }

        void Load(Screenplay sp)
        {
            _screenplay = sp;
            if (_view == null) return;
            _view.Populate(sp);
            if (_header != null) _header.text = sp != null ? "Screenplay: " + sp.name : "No Screenplay loaded";
        }
    }

    public class StoryGraphView : GV.GraphView
    {
        Screenplay _sp;

        public StoryGraphView()
        {
            SetupZoom(GV.ContentZoomer.DefaultMinScale, GV.ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new GV.ContentDragger());
            this.AddManipulator(new GV.SelectionDragger());
            this.AddManipulator(new GV.RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = OnGraphChanged;

            // Live traversal visualisation while playing: poll the running StoryRunner(s) a few times a second.
            schedule.Execute(UpdateRuntimeViz).Every(120);
        }

        bool _vizOn;

        void UpdateRuntimeViz()
        {
            if (!Application.isPlaying)
            {
                if (_vizOn) { ClearViz(); _vizOn = false; }
                return;
            }
            _vizOn = true;
            var runners = UnityEngine.Object.FindObjectsByType<StoryRunner>(FindObjectsSortMode.None);

            foreach (var n in nodes.ToList().OfType<StoryNodeView>())
            {
                bool cur = false, vis = false;
                foreach (var r in runners)
                {
                    if (r.IsCurrent(n.Page.Id)) { cur = true; break; }
                    if (r.WasVisited(n.Page.Id)) vis = true;
                }
                n.SetViz(cur ? 2 : vis ? 1 : 0);
            }

            foreach (var e in edges.ToList())
            {
                var o = e.output?.node as StoryNodeView;
                var i = e.input?.node as StoryNodeView;
                bool trav = o != null && i != null && runners.Any(r => r.WasEdgeTraversed(o.Page.Id, e.output.portName, i.Page.Id));
                SetEdgeViz(e, trav);
            }
        }

        void ClearViz()
        {
            foreach (var n in nodes.ToList().OfType<StoryNodeView>()) n.SetViz(0);
            foreach (var e in edges.ToList()) SetEdgeViz(e, false);
        }

        static void SetEdgeViz(GV.Edge e, bool traversed)
        {
            if (e?.edgeControl == null) return;
            var col = traversed ? new Color(0.30f, 0.85f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
            e.edgeControl.inputColor = col;
            e.edgeControl.outputColor = col;
            e.edgeControl.edgeWidth = traversed ? 4 : 2;
        }

        public void Populate(Screenplay sp)
        {
            _sp = sp;
            DeleteElements(graphElements.ToList());
            if (sp == null) return;

            var map = new Dictionary<string, StoryNodeView>();
            foreach (var p in sp.Pages)
            {
                if (p == null) continue;
                if (string.IsNullOrEmpty(p.Id)) p.Id = System.Guid.NewGuid().ToString("N");
                var nv = CreateNode(p);
                AddElement(nv);
                map[p.Id] = nv;
            }
            if (sp.Edges != null)
                foreach (var e in sp.Edges)
                {
                    if (!map.TryGetValue(e.From, out var from) || !map.TryGetValue(e.To, out var to)) continue;
                    var op = from.OutPort(e.Port);
                    var ip = to.InPort;
                    if (op == null || ip == null) continue;
                    var edge = op.ConnectTo(ip);
                    AddElement(edge);
                }
        }

        StoryNodeView CreateNode(Page page)
        {
            var nv = new StoryNodeView(page, _sp);
            nv.SetPosition(new Rect(page.GraphPos, new Vector2(200, 120)));
            return nv;
        }

        public override List<GV.Port> GetCompatiblePorts(GV.Port startPort, GV.NodeAdapter nodeAdapter)
        {
            var compatible = new List<GV.Port>();
            ports.ForEach(p =>
            {
                if (startPort != p && startPort.node != p.node && startPort.direction != p.direction)
                    compatible.Add(p);
            });
            return compatible;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (_sp != null)
            {
                Vector2 mouse = contentViewContainer.WorldToLocal(evt.localMousePosition);
                foreach (var t in TypeCache.GetTypesDerivedFrom<Page>())
                {
                    if (t.IsAbstract) continue;
                    var type = t;
                    evt.menu.AppendAction("Add ▸ " + Pretty(type.Name), _ => AddPage(type, mouse));
                }
                evt.menu.AppendSeparator();
                if (selection.OfType<StoryNodeView>().FirstOrDefault() is StoryNodeView sel)
                    evt.menu.AppendAction("Set as Entry", _ => { _sp.EntryId = sel.Page.Id; EditorUtility.SetDirty(_sp); Populate(_sp); });
            }
            base.BuildContextualMenu(evt);
        }

        void AddPage(System.Type type, Vector2 pos)
        {
            var page = (Page)System.Activator.CreateInstance(type);
            page.Id = System.Guid.NewGuid().ToString("N");
            page.GraphPos = pos;
            if (string.IsNullOrEmpty(page.Title)) page.Title = Pretty(type.Name);
            _sp.Pages.Add(page);
            if (string.IsNullOrEmpty(_sp.EntryId) && page is EntryPage) _sp.EntryId = page.Id;
            var nv = CreateNode(page);
            AddElement(nv);
            EditorUtility.SetDirty(_sp);
        }

        GV.GraphViewChange OnGraphChanged(GV.GraphViewChange change)
        {
            // Persist after the view has applied the change (edges/nodes settled).
            EditorApplication.delayCall += () => { if (this != null && _sp != null) SaveToAsset(); };
            return change;
        }

        public void SaveToAsset()
        {
            if (_sp == null) return;
            var nodeViews = nodes.ToList().OfType<StoryNodeView>().ToList();

            _sp.Pages = nodeViews.Select(n => n.Page).ToList();
            foreach (var n in nodeViews) n.Page.GraphPos = n.GetPosition().position;

            _sp.Edges = new List<Edge>();
            foreach (var e in edges.ToList())
            {
                var o = e.output?.node as StoryNodeView;
                var i = e.input?.node as StoryNodeView;
                if (o == null || i == null) continue;
                _sp.Edges.Add(new Edge(o.Page.Id, e.output.portName, i.Page.Id));
            }

            if (_sp.GetPage(_sp.EntryId) == null) _sp.EntryId = _sp.ResolveEntry();
            EditorUtility.SetDirty(_sp);
        }

        public void FrameAll() => FrameAll(true);
        void FrameAll(bool _) { schedule.Execute(() => base.FrameAll()).ExecuteLater(1); }

        static string Pretty(string typeName)
        {
            if (typeName.EndsWith("Page")) typeName = typeName.Substring(0, typeName.Length - 4);
            return typeName;
        }
    }

    // One node = one Page. Input on the left (many edges in), one output per Page.Ports on the right.
    public class StoryNodeView : GV.Node
    {
        public readonly Page Page;
        public GV.Port InPort { get; private set; }
        readonly Dictionary<string, GV.Port> _outs = new Dictionary<string, GV.Port>();
        readonly Screenplay _sp;

        public StoryNodeView(Page page, Screenplay sp)
        {
            Page = page;
            _sp = sp;
            style.minWidth = 240;
            title = TitleText();

            var typeLabel = new Label(page.GetType().Name) { style = { opacity = 0.6f, marginLeft = 6, marginBottom = 2, fontSize = 10 } };
            mainContainer.Add(typeLabel);

            if (!(page is EntryPage))
            {
                InPort = GV.Port.Create<GV.Edge>(GV.Orientation.Horizontal, GV.Direction.Input, GV.Port.Capacity.Multi, typeof(bool));
                InPort.portName = "in";
                inputContainer.Add(InPort);
            }

            var ports = page.Ports;
            if (ports != null)
                foreach (var pn in ports)
                {
                    var op = GV.Port.Create<GV.Edge>(GV.Orientation.Horizontal, GV.Direction.Output, GV.Port.Capacity.Multi, typeof(bool));
                    op.portName = pn;
                    _outs[pn] = op;
                    outputContainer.Add(op);
                }

            BuildFields();

            expanded = true;
            RefreshExpandedState();
            RefreshPorts();
        }

        string TitleText()
        {
            string mark = (_sp != null && _sp.EntryId == Page.Id) ? "▶ " : "";
            return mark + (string.IsNullOrEmpty(Page.Title) ? Page.GetType().Name : Page.Title);
        }

        void MarkDirty() { if (_sp != null) EditorUtility.SetDirty(_sp); }

        // Reflection-driven, in-node editors for the Page's public fields. Scalars / bool / enum are edited
        // right on the canvas; complex fields (lists, [SerializeReference] rules, Vector2) point to the
        // Inspector, where Unity's polymorphic SerializeReference UI handles them.
        void BuildFields()
        {
            var section = new VisualElement { style = { paddingLeft = 6, paddingRight = 6, paddingTop = 2, paddingBottom = 4 } };
            foreach (var fi in Page.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (fi.Name == "GraphPos") continue; // node position is the canvas itself
                section.Add(BuildField(fi, Page));
            }
            extensionContainer.Add(section);
        }

        // Builds an editor for field `fi` on object `target` (the Page, or — recursively — a list element).
        VisualElement BuildField(FieldInfo fi, object target)
        {
            var t = fi.FieldType;
            string name = fi.Name;

            if (t == typeof(string))
            {
                bool multi = fi.GetCustomAttribute<TextAreaAttribute>() != null;
                var f = new TextField(name) { value = (string)fi.GetValue(target) ?? "", multiline = multi };
                f.labelElement.style.minWidth = 70;
                f.RegisterValueChangedCallback(e =>
                {
                    fi.SetValue(target, e.newValue);
                    if (name == "Title" && target == (object)Page) title = TitleText();
                    MarkDirty();
                });
                return f;
            }
            if (t == typeof(int))
            {
                var f = new IntegerField(name) { value = (int)fi.GetValue(target) };
                f.labelElement.style.minWidth = 70;
                f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); MarkDirty(); });
                return f;
            }
            if (t == typeof(float))
            {
                var f = new FloatField(name) { value = (float)fi.GetValue(target) };
                f.labelElement.style.minWidth = 70;
                f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); MarkDirty(); });
                return f;
            }
            if (t == typeof(bool))
            {
                var f = new Toggle(name) { value = (bool)fi.GetValue(target) };
                f.labelElement.style.minWidth = 70;
                f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); MarkDirty(); });
                return f;
            }
            if (t.IsEnum)
            {
                var f = new EnumField(name, (Enum)fi.GetValue(target));
                f.labelElement.style.minWidth = 70;
                f.RegisterValueChangedCallback(e => { fi.SetValue(target, e.newValue); MarkDirty(); });
                return f;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return BuildList(fi, target);

            return new Label(name + ": (edit in Inspector)")
            { style = { opacity = 0.5f, fontSize = 10, marginTop = 2, whiteSpace = WhiteSpace.Normal } };
        }

        // In-node editor for a List<T> of [Serializable] elements (e.g. a Decision's Options or a PlotTwist's
        // Ops): add / remove rows, each row exposing the element's own fields. Re-renders itself on change.
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
                head.Add(new Button(() => { EnsureList(fi, target).Add(NewElem(elemType)); MarkDirty(); rebuild(); }) { text = "+ Add" });
                box.Add(head);

                var list = fi.GetValue(target) as System.Collections.IList;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        int idx = i;
                        var item = list[idx];
                        var row = new VisualElement { style = { marginBottom = 3, paddingLeft = 3, paddingBottom = 2, borderLeftWidth = 1, borderLeftColor = new Color(1f, 1f, 1f, 0.1f) } };
                        var rowHead = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                        rowHead.Add(new Label("#" + idx) { style = { flexGrow = 1, fontSize = 10, opacity = 0.6f } });
                        rowHead.Add(new Button(() => { EnsureList(fi, target).RemoveAt(idx); MarkDirty(); rebuild(); }) { text = "✕" });
                        row.Add(rowHead);
                        if (item != null)
                            foreach (var efi in elemType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                                row.Add(BuildField(efi, item));
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

        static object NewElem(Type elemType)
        {
            if (elemType == typeof(string)) return "";
            return Activator.CreateInstance(elemType);
        }

        public GV.Port OutPort(string name)
        {
            if (name != null && _outs.TryGetValue(name, out var p)) return p;
            return _outs.Values.FirstOrDefault();
        }

        // Live traversal styling: 0 = untouched, 1 = visited (soft green border), 2 = current bookmark
        // (bright thick border + warm title tint, so the active page is unmistakable).
        public void SetViz(int state)
        {
            Color border; float w;
            if (state == 2) { border = new Color(1f, 0.85f, 0.15f); w = 4f; }
            else if (state == 1) { border = new Color(0.30f, 0.80f, 0.45f); w = 2f; }
            else { border = Color.clear; w = 0f; }

            style.borderTopColor = border; style.borderBottomColor = border;
            style.borderLeftColor = border; style.borderRightColor = border;
            style.borderTopWidth = w; style.borderBottomWidth = w;
            style.borderLeftWidth = w; style.borderRightWidth = w;

            titleContainer.style.backgroundColor = state == 2
                ? new StyleColor(new Color(0.55f, 0.45f, 0.05f, 0.55f))
                : new StyleColor(StyleKeyword.Null);
        }
    }
}
