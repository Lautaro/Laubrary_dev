using System;
using System.Collections.Generic;
using System.Text;
using Laubrary.Zounds.Dsp;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using G = Laubrary.Zounds.ChainEditorGUI;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the effect-chain editor (T-0462 onward): the library bar, the reserved error row, the effect
    /// rows (grip, On, name, then every setting inline or a summary that expands into wrapped rows), Add effect and the
    /// tail, the Modifiers header, the source-stage rows, each modifier's row, settings, curve ground and bindings.
    ///
    /// It shares the old editor's non-drawing logic outright — setting widths, the inline-or-expand rule, number
    /// formats, hover texts, the modulation menu, every edit path — so the two cannot disagree about what a row holds or
    /// where it wraps. Rows are placed the way the old code places rects: fixed offsets inside a 20 px row, with the remove
    /// button pinned to the right edge.
    ///
    /// The tree is rebuilt only when the chain's STRUCTURE changes (effects, modifiers, bindings, overrides, what is
    /// folded or expanded, where rows wrap); a value change refreshes the controls in place, so a drag never loses the
    /// slider it is dragging.
    /// </summary>
    public class ChainEditorTK : VisualElement {

        readonly Zound zound;
        int selectedNode = -1;
        /// <summary>Which effect's settings are expanded (-1: none), as in the old editor.</summary>
        internal int SelectedNode { get => selectedNode; set { selectedNode = value; Tick(); } }
        readonly HashSet<ZoundModifier> folded = new HashSet<ZoundModifier>();
        readonly List<Action> refreshers = new List<Action>();
        readonly List<Action> liveRefreshers = new List<Action>();
        string builtSig;
        bool dragUndoOpen;

        // node drag-reorder
        readonly List<VisualElement> nodeRows = new List<VisualElement>();
        VisualElement nodesBox, dropLine;
        int dragNode = -1, dragTarget = -1;

        // what every modifier outputs across one play (for a curve's ground and the slow-rate warning), cached per revision
        EditorTools.ChainSpectrumProbe.Measurement modulation;
        int modulationVersion = int.MinValue;

        public ChainEditorTK(Zound zound) {
            this.zound = zound;
            AddToClassList("zs-chain");
            style.flexShrink = 0;
            RegisterCallback<GeometryChangedEvent>(_ => Tick());
            // Continuous edits hold one Undo step from the first change to the release, as the old editor's do.
            RegisterCallback<PointerUpEvent>(_ => EndDrag(), TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag(), TrickleDown.TrickleDown);
            schedule.Execute(Tick).Every(33);
        }

        float Width => float.IsNaN(resolvedStyle.width) ? 0f : resolvedStyle.width;

        // ─────────────────────────── edits (the old editor's paths) ───────────────────────────

        void Modify(string undo, Action action) { G.Modify(zound, undo, action); Tick(); }

        void ModifyContinuous(string undo, Action action) {
            if (!dragUndoOpen) { dragUndoOpen = true; ZoundsWindow.BeginDragUndo(undo); }
            action();
            ZoundDspPlayback.InvalidateLayout(zound);
            EditorUtility.SetDirty(ZoundsProject.Instance);
        }

        void EndDrag() {
            if (dragUndoOpen) { dragUndoOpen = false; ZoundsWindow.EndDragUndo(); }
        }

        // ─────────────────────────── build / refresh ───────────────────────────

        void Tick() {
            if (panel == null) return;
            float w = Width;
            if (w <= 0f) return;
            var chain = ZoundDspPlayback.ResolveChain(zound, out var preset);
            if (chain == null) return;
            string sig = Signature(chain, preset, w);
            if (sig != builtSig) { builtSig = sig; Build(chain, preset, w); }
            foreach (var r in refreshers) r();
            foreach (var r in liveRefreshers) r();
        }

        void EnsureModulation(ZoundEffectChain chain) {
            if (modulationVersion == chain.version && modulation.modifierOutput != null) return;
            modulationVersion = chain.version;
            if (!ZoundSapPlayback.TryGetPlayLength(zound, out float play)) play = 1.5f;
            modulation = EditorTools.ChainSpectrumProbe.MeasureModulation(chain, play);
        }

        float AvailableWidth(float w) => Mathf.Max(200f, w);
        static float HeaderLeadW => G.GripW + 2f + G.OnW + 6f + G.NameW + G.Gap;

        /// <summary>Everything that decides the tree's shape, as one string: when it changes the tree is rebuilt.</summary>
        string Signature(ZoundEffectChain chain, ZoundChainPreset preset, float w) {
            bool linked = preset != null;
            var sb = new StringBuilder();
            sb.Append(preset != null ? preset.id.ToString() : "local").Append('|');
            sb.Append(preset == null && ZoundChainLibrary.CanReconnect(zound) ? 'R' : 'r');
            sb.Append(ZoundEffectDescriptors.TailBudgetSeconds(chain) > 0f ? 'T' : 't').Append('|');
            sb.Append(selectedNode).Append('|');
            for (int i = 0; i < chain.nodes.Count; i++) {
                var node = chain.nodes[i]; node.EnsureParams();
                var desc = ZoundEffectDescriptors.Get(node.type);
                var units = NodeUnits(chain, i, node, desc, linked);
                sb.Append((int)node.type).Append(node.enabled ? '+' : '-');
                foreach (var u in units) sb.Append(u.overridden ? 'o' : '.').Append(u.bound ? 'b' : '.');
                sb.Append(Inline(units, w) ? 'I' : 'E');
                if (!Inline(units, w) && selectedNode == i) sb.Append(ZUI.WrapRow.CountRows(AvailableWidth(w) - (G.GripW + 6f), G.Gap, Widths(units)));
                sb.Append(';');
            }
            sb.Append('|');
            foreach (var b in chain.bindings)
                sb.Append(b.modifierIndex).Append(',').Append(b.nodeIndex).Append(',').Append(b.paramIndex).Append(',').Append((int)ChainModulationCompat.CombineOf(b)).Append(';');
            sb.Append('|');
            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m]; mod.EnsureParams();
                sb.Append((int)mod.type).Append(mod.enabled ? '+' : '-').Append(folded.Contains(mod) ? 'F' : 'U');
                if (!folded.Contains(mod)) {
                    var units = ModifierUnits(chain, m, mod, ZoundEffectDescriptors.GetModifier(mod.type));
                    foreach (var u in units) sb.Append(u.paramIndex).Append(u.warning != null ? 'w' : '.');
                    sb.Append('r').Append(ZUI.WrapRow.CountRows(AvailableWidth(w) - (G.GripW + 6f), G.Gap, Widths(units)));
                    if (mod.type == ZoundModifierType.Step) sb.Append('s').Append(mod.steps?.Length ?? 0);
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        static List<float> Widths(List<G.ParamUnit> units) { var l = new List<float>(units.Count); foreach (var u in units) l.Add(u.width); return l; }

        bool Inline(List<G.ParamUnit> units, float w)
            => ZUI.WrapRow.OneRowWidth(G.Gap, Widths(units)) <= AvailableWidth(w) - HeaderLeadW - G.RemoveW - G.Gap;

        void Build(ZoundEffectChain chain, ZoundChainPreset preset, float w) {
            EndDrag();
            Clear();
            refreshers.Clear(); liveRefreshers.Clear(); nodeRows.Clear();
            bool linked = preset != null;

            Add(LibraryBar(chain, preset));
            Add(ErrorRow(chain, linked));
            Add(Nodes(chain, linked, w));
            Add(AddEffectRow(chain));
            Add(VSpace(5f));   // ZUI.RowSpace(0.5f)
            Add(ModifiersHeader(chain));
            for (int k = 0; k < SourceStageParam.Count; k++) {
                if (!G.IsBound(chain, -1, k)) continue;
                Add(SourceStageRow(chain, k));
            }
            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m];
                Add(ModifierRow(chain, m, mod));
                if (!folded.Contains(mod)) {
                    ModifierBody(chain, m, mod, w);
                    Bindings(chain, m);
                }
            }
            Add(VSpace(5f));   // ZUI.RowSpace(0.5f), then the analyser (T-0467)
        }

        // ─────────────────────────── small element helpers ───────────────────────────

        static VisualElement VSpace(float h) { var e = new VisualElement(); e.style.height = h; e.style.flexShrink = 0; return e; }

        static VisualElement Row(float h = G.RowH) {
            var r = new VisualElement();
            r.AddToClassList("zs-chain-row");
            r.style.height = h; r.style.flexShrink = 0;
            return r;
        }

        static VisualElement HRow(float h = G.RowH) {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = h; r.style.flexShrink = 0;
            return r;
        }

        static T Place<T>(T e, float x, float y, float w, float h) where T : VisualElement {
            e.style.position = Position.Absolute;
            e.style.left = x; e.style.top = y;
            if (w >= 0f) e.style.width = w;
            if (h >= 0f) e.style.height = h;
            return e;
        }

        static T PlaceRight<T>(T e, float right, float y, float w, float h) where T : VisualElement {
            e.style.position = Position.Absolute;
            e.style.right = right; e.style.top = y; e.style.width = w; e.style.height = h;
            return e;
        }

        static Label Text(string text, string tooltip, params string[] classes) {
            var l = new Label(text) { tooltip = tooltip };
            l.AddToClassList("zs-lbl");
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        static VisualElement Fill(Color c) {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
            e.style.backgroundColor = c;
            return e;
        }

        // ─────────────────────────── library bar ───────────────────────────

        VisualElement LibraryBar(ZoundEffectChain chain, ZoundChainPreset preset) {
            var r = HRow();
            var title = Text("", "", "zs-text-subheader", "zs-subheader");
            title.style.width = 220f;
            refreshers.Add(() => {
                var ch = ZoundDspPlayback.ResolveChain(zound, out var p);
                int users = p != null ? ZoundChainLibrary.CountUsers(p.id) : 0;
                title.text = p != null ? p.name + "  (" + users + (users == 1 ? " user)" : " users)") : "Local chain";
                title.tooltip = p != null
                    ? "This zound plays the library preset '" + p.name + "' by live reference: editing the nodes below changes every zound using it."
                    : "This zound's own chain. Save it as a preset to share it with other zounds.";
            });
            r.Add(title);
            r.Add(Flex());
            Button lib = null, save = null;
            lib = ZS.Button("Library…", "Browse the chain presets: use one on this zound, audition it, rename, duplicate or delete presets.", "RichButton",
                            () => ChainLibraryPopup.Show(lib.worldBound, zound), ZUICornerMask.Left, 70f, G.RowH);
            save = ZS.Button("Save as…", "Saves a copy of this chain as a new library preset and links this zound to it.", "RichButton",
                             () => SavePresetPopup.Show(save.worldBound, zound.name + " chain", name => Modify("save chain as preset", () => {
                                 var p = ZoundChainLibrary.Create(name, ZoundDspPlayback.ResolveChain(zound, out _));
                                 ZoundChainLibrary.Assign(zound, p);
                             })), ZUICornerMask.None, 70f, G.RowH);
            r.Add(lib); r.Add(save);
            if (preset != null) {
                r.Add(ZS.Button("Detach", "Breaks the link: this zound keeps a private copy of the preset (with its overrides folded in) and stops following preset edits.", "RichButton",
                                () => Modify("detach chain preset", () => ZoundChainLibrary.Detach(zound)), ZUICornerMask.Right, 62f, G.RowH));
            }
            else {
                bool canReconnect = ZoundChainLibrary.CanReconnect(zound);
                var rp = ZoundChainLibrary.Find(zound.detachedChainPresetId);
                var b = ZS.Button("Reconnect", canReconnect ? "Follows the preset '" + rp.name + "' again; this zound's private copy is discarded." : "Nothing to reconnect to: this chain was not detached from a preset.",
                                  "RichButton", () => Modify("reconnect chain preset", () => ZoundChainLibrary.Reconnect(zound)), ZUICornerMask.Right, 78f, G.RowH);
                b.SetEnabled(canReconnect);
                r.Add(b);
            }
            return r;
        }

        static VisualElement Flex() { var e = new VisualElement(); e.style.flexGrow = 1; return e; }

        // ─────────────────────────── error row ───────────────────────────

        VisualElement ErrorRow(ZoundEffectChain chain, bool linked) {
            var r = Row(EditorGUIUtility.singleLineHeight);
            var bar = Place(new VisualElement(), 0f, 0f, 3f, -1f);
            bar.style.bottom = 0; bar.style.backgroundColor = new Color(0.95f, 0.75f, 0.3f);
            var msg = Place(Text("", "", "zs-mini", "zs-warntext"), 7f, 0f, -1f, -1f);
            msg.style.right = 0; msg.style.bottom = 0;
            r.Add(bar); r.Add(msg);
            refreshers.Add(() => {
                var ch = ZoundDspPlayback.ResolveChain(zound, out var p);
                var err = ChainLayout.Build(ch, AudioSettings.outputSampleRate, p != null ? zound.chainOverrides : null).error;
                bar.style.display = msg.style.display = err != null ? DisplayStyle.Flex : DisplayStyle.None;
                msg.text = err != null ? "⚠ " + err : ""; msg.tooltip = err ?? "";
            });
            return r;
        }

        // ─────────────────────────── effect rows ───────────────────────────

        List<G.ParamUnit> NodeUnits(ZoundEffectChain chain, int nodeIndex, ZoundEffectNode node, EffectDesc desc, bool linked) {
            var list = new List<G.ParamUnit>(desc.parameters.Length);
            for (int k = 0; k < desc.parameters.Length; k++) {
                int pk = k;
                var u = new G.ParamUnit {
                    pd = desc.parameters[k], nodeIndex = nodeIndex, paramIndex = k,
                    value = G.EffectiveValue(zound, chain, nodeIndex, k, node.p[k]),
                    overridden = linked && ZoundChainLibrary.TryGetOverride(zound, nodeIndex, k, out _),
                    bound = G.IsBound(chain, nodeIndex, k),
                    onDrag = v => {
                        if (linked) ModifyContinuous("override chain parameter", () => ZoundChainLibrary.SetOverride(zound, nodeIndex, pk, v));
                        else ModifyContinuous("change effect parameter", () => { node.p[pk] = v; chain.Touch(); });
                        ZoundDspPlayback.PushLiveParam(zound, chain, nodeIndex, pk, v);
                    },
                    onSet = v => {
                        if (linked) Modify("override chain parameter", () => ZoundChainLibrary.SetOverride(zound, nodeIndex, pk, v));
                        else Modify("change effect parameter", () => { node.p[pk] = v; chain.Touch(); });
                        ZoundDspPlayback.PushLiveParam(zound, chain, nodeIndex, pk, v);
                    },
                };
                u.width = G.UnitWidth(u);
                list.Add(u);
            }
            return list;
        }

        /// <summary>A unit's current value, read fresh on every refresh (the unit object is only a build-time snapshot).</summary>
        Func<float> NodeValue(int nodeIndex, int k) => () => {
            var ch = ZoundDspPlayback.ResolveChain(zound, out _);
            if (ch == null || nodeIndex >= ch.nodes.Count) return 0f;
            var n = ch.nodes[nodeIndex]; n.EnsureParams();
            return k < n.p.Length ? G.EffectiveValue(zound, ch, nodeIndex, k, n.p[k]) : 0f;
        };

        VisualElement Nodes(ZoundEffectChain chain, bool linked, float w) {
            nodesBox = new VisualElement();
            nodesBox.style.flexShrink = 0;
            if (chain.nodes.Count == 0) {
                var none = Text("No effects.", "", "zs-subtle");
                none.style.height = EditorGUIUtility.singleLineHeight;
                nodesBox.Add(none);
            }
            for (int i = 0; i < chain.nodes.Count; i++) {
                int ni = i;
                var node = chain.nodes[i];
                var desc = ZoundEffectDescriptors.Get(node.type);
                var units = NodeUnits(chain, i, node, desc, linked);
                bool inline = Inline(units, w);
                bool selected = selectedNode == i;

                var row = Row();
                nodeRows.Add(row);
                nodesBox.Add(row);
                if (selected && !inline) row.Add(Fill(new Color(1f, 1f, 1f, 0.04f)));

                // grip
                var grip = Place(Text("≡", "Drag to reorder. Signal flows top to bottom.", "zs-greymini"), 0f, 0f, G.GripW, G.RowH);
                grip.AddToClassList("zs-grip");
                grip.RegisterCallback<PointerDownEvent>(e => {
                    if (e.button != 0) return;
                    dragNode = ni; dragTarget = ni; grip.CapturePointer(e.pointerId); ShowDropLine(); e.StopPropagation();
                });
                grip.RegisterCallback<PointerMoveEvent>(e => {
                    if (dragNode < 0 || !grip.HasPointerCapture(e.pointerId)) return;
                    dragTarget = DropIndexAt(nodesBox.WorldToLocal(e.position).y); ShowDropLine();
                });
                grip.RegisterCallback<PointerUpEvent>(e => {
                    if (dragNode < 0) return;
                    if (grip.HasPointerCapture(e.pointerId)) grip.ReleasePointer(e.pointerId);
                    FinishNodeDrag();
                });
                row.Add(grip);

                // enable
                ZuiToggleButton on = null;
                on = ZS.Toggle("On", node.enabled ? "Bypass this effect (its state is kept)." : "Enable this effect.", node.enabled,
                               v => Modify(v ? "enable effect" : "bypass effect", () => { chain.nodes[ni].enabled = v; chain.Touch(); }),
                               "RichToggle", ZUICornerMask.None, G.OnW, G.RowH - 2f);
                row.Add(Place(on, G.GripW + 2f, 1f, G.OnW, G.RowH - 2f));

                float nameX = G.GripW + 2f + G.OnW + 6f;
                var name = Place(Text(desc.displayName, desc.summary, "zs-bold"), nameX, 0f, G.NameW, G.RowH);
                row.Add(name);

                if (inline) {
                    float x = nameX + G.NameW + G.Gap;
                    foreach (var u in units) {
                        row.Add(Unit(chain, u, NodeValue(ni, u.paramIndex), x, 1f, u.width, G.RowH - 2f));
                        x += u.width + G.Gap;
                    }
                }
                else {
                    var summary = Place(Text("", "", "zs-mini"), nameX + G.NameW, 0f, -1f, G.RowH);
                    summary.style.right = G.RemoveW + 4f;
                    summary.tooltip = selected ? "Click to fold the settings away." : "Click to show all " + units.Count + " settings. They do not fit on this row at the window's current width.";
                    refreshers.Add(() => {
                        var ch = ZoundDspPlayback.ResolveChain(zound, out _);
                        if (ni >= ch.nodes.Count) return;
                        summary.text = (selected ? "▾ " : "▸ ") + G.Summary(zound, ch, ni, ch.nodes[ni], ZoundEffectDescriptors.Get(ch.nodes[ni].type));
                    });
                    EventCallback<PointerDownEvent> toggle = e => { if (e.button != 0) return; selectedNode = selected ? -1 : ni; e.StopPropagation(); Tick(); };
                    summary.RegisterCallback(toggle);
                    name.RegisterCallback(toggle);
                    row.Add(summary);
                }

                // remove
                row.Add(PlaceRight(ZS.Button("×", "Removes this effect from the chain.", "RichButton",
                    () => Modify("remove effect", () => { chain.RemoveNode(ni); if (selectedNode >= chain.nodes.Count) selectedNode = -1; }),
                    ZUICornerMask.All, G.RemoveW, G.RowH - 2f), 0f, 1f, G.RemoveW, G.RowH - 2f));

                if (!inline && selected) Wrap(chain, units, u => NodeValue(ni, u.paramIndex), w, nodesBox);
            }
            dropLine = new VisualElement { pickingMode = PickingMode.Ignore };
            dropLine.style.position = Position.Absolute; dropLine.style.left = 0; dropLine.style.right = 0; dropLine.style.height = 2f;
            dropLine.style.backgroundColor = new Color(0.4f, 0.8f, 1f, 0.9f);
            dropLine.style.display = DisplayStyle.None;
            nodesBox.Add(dropLine);
            return nodesBox;
        }

        /// <summary>The old editor's WrapRow: units flow left to right from the indent, a new 20 px row when the next does not fit.</summary>
        void Wrap(ZoundEffectChain chain, List<G.ParamUnit> units, Func<G.ParamUnit, Func<float>> value, float w, VisualElement into) {
            float indent = G.GripW + 6f;
            float avail = Mathf.Max(1f, AvailableWidth(w) - indent);
            VisualElement row = null; float used = 0f;
            foreach (var u in units) {
                float need = row != null && used > 0f ? G.Gap + u.width : u.width;
                if (row == null || used + need > avail + 0.01f) { row = Row(); into.Add(row); used = 0f; need = u.width; }
                row.Add(Unit(chain, u, value(u), indent + used + (need - u.width), 1f, u.width, G.RowH - 2f));
                used += need;
            }
        }

        int DropIndexAt(float y) {
            for (int i = 0; i < nodeRows.Count; i++) {
                var l = nodeRows[i].layout;
                if (y < l.y + l.height * 0.5f) return i;
            }
            return nodeRows.Count;
        }

        void ShowDropLine() {
            if (dropLine == null || nodeRows.Count == 0 || dragTarget < 0) { if (dropLine != null) dropLine.style.display = DisplayStyle.None; return; }
            float y = dragTarget < nodeRows.Count ? nodeRows[dragTarget].layout.y : nodeRows[nodeRows.Count - 1].layout.yMax;
            dropLine.style.top = y - 1f;
            dropLine.style.display = DisplayStyle.Flex;
            dropLine.BringToFront();
            foreach (var r in nodeRows) r.RemoveFromClassList("zs-dragging");
            if (dragNode >= 0 && dragNode < nodeRows.Count) nodeRows[dragNode].AddToClassList("zs-dragging");
        }

        void FinishNodeDrag() {
            int from = dragNode, to = dragTarget;
            dragNode = -1; dragTarget = -1;
            if (dropLine != null) dropLine.style.display = DisplayStyle.None;
            foreach (var r in nodeRows) r.RemoveFromClassList("zs-dragging");
            if (from < 0 || to < 0) return;
            if (to > from) to--;
            var chain = ZoundDspPlayback.ResolveChain(zound, out _);
            if (to == from || to >= chain.nodes.Count) return;
            Modify("reorder effects", () => { chain.MoveNode(from, to); selectedNode = to; });
        }

        VisualElement AddEffectRow(ZoundEffectChain chain) {
            var r = HRow();
            r.Add(ZS.Button("Add effect…", "Appends an effect to the end of the chain.", "RichButton", () => {
                var items = new List<ZUI.ZUIMenuItem>();
                for (int t = 0; t < ZoundEffectDescriptors.EffectTypeCount; t++) {
                    var type = (ZoundEffectType)t;
                    items.Add(ZUI.MenuItem(ZoundEffectDescriptors.Get(type).displayName, () => Modify("add effect", () => {
                        var ch = ZoundDspPlayback.ResolveChain(zound, out _);
                        ch.nodes.Add(new ZoundEffectNode(type)); ch.Touch(); selectedNode = ch.nodes.Count - 1;
                    })));
                }
                ZUI.ContextMenu(items.ToArray());
            }, ZUICornerMask.All, 90f, G.RowH));
            r.Add(Flex());
            if (ZoundEffectDescriptors.TailBudgetSeconds(chain) > 0f) {
                var tail = Text("", "How long this chain keeps ringing after the source stops (delay and reverb decay). Audio End waits at most this long.", "zs-mini");
                tail.style.width = 110f; tail.style.height = G.RowH;
                refreshers.Add(() => tail.text = "tail " + ZoundEffectDescriptors.TailBudgetSeconds(ZoundDspPlayback.ResolveChain(zound, out _)).ToString("0.00") + " s");
                r.Add(tail);
            }
            return r;
        }

        // ─────────────────────────── one setting ───────────────────────────

        /// <summary>
        /// One setting as its own labelled control, placed at (x, y) in its row: a slider with its name and value in the
        /// track, a toggle whose face is its name, a choice strip, or a strip of icons; then the "~" tag when modulated and
        /// the warning mark. Right-click anywhere on it opens its modulation menu.
        /// </summary>
        VisualElement Unit(ZoundEffectChain chain, G.ParamUnit u, Func<float> read, float x, float y, float w, float h) {
            var pd = u.pd;
            var box = Place(new VisualElement(), x, y, w, h);
            float cw = w - (u.bound ? G.TagIconW : 0f) - (u.warning != null ? G.WarnIconW : 0f);
            bool isEffect = u.nodeIndex != int.MinValue;
            string tip = G.ParamTip(pd, u.overridden, isEffect && pd.automatable);

            if (u.pdHigh.name != null) {
                float Lo() => Mathf.Clamp(read(), pd.min, pd.max);
                var mm = ZS.MinMax(pd.name + "–" + u.pdHigh.name, u.value, u.valueHigh, pd.min, pd.max,
                                   pd.name + " and " + u.pdHigh.name + ": the range a value is drawn from, one per play. " + pd.desc,
                                   (lo, hi) => u.onRange(lo, hi), "Default", ZuiSkinMinMax.LabelMode.LabelAndValues, false, cw, h);
                box.Add(Place(mm, 0f, 0f, cw, h));
                var hiRead = u.readHigh;
                refreshers.Add(() => { if (hiRead != null) mm.SetValuesWithoutNotify(Lo(), hiRead()); });
            }
            else if (u.icons != null || pd.IsChoice) {
                int n = pd.options.Length;
                var ow = new float[n]; float total = 0f;
                for (int o = 0; o < n; o++) {
                    ow[o] = u.icons != null ? ZUI.IconChoiceCellWidth : Mathf.Max(38f, G.measureStyle.CalcSize(new GUIContent(pd.options[o])).x + 14f);
                    total += ow[o];
                }
                float scale = cw / Mathf.Max(1f, total), ox = 0f;
                var toggles = new ZuiToggleButton[n];
                for (int o = 0; o < n; o++) {
                    int oi = o;
                    var corner = n == 1 ? ZUICornerMask.All : o == 0 ? ZUICornerMask.Left : o == n - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                    string otip = pd.name + ": " + (pd.OptionTip(o) ?? pd.options[o]);
                    ZuiToggleButton t = null;
                    t = ZS.Toggle(u.icons != null ? "" : pd.options[o], otip, Cur() == o, v => {
                        if (Cur() == oi) { t.SetValueWithoutNotify(true); return; }
                        u.onSet(oi);
                    }, "RichToggle", corner, ow[o] * scale, h);
                    if (u.icons != null && o < u.icons.Length) {
                        t.markWhenOn = false;
                        var img = new Image { image = u.icons[o], scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                        img.AddToClassList("zs-iconchoice__icon");
                        t.Add(img);
                    }
                    toggles[o] = t;
                    box.Add(Place(t, ox, 0f, ow[o] * scale, h));
                    ox += ow[o] * scale;
                }
                int Cur() => Mathf.Clamp(Mathf.RoundToInt(read()), 0, n - 1);
                refreshers.Add(() => { int c = Cur(); for (int o = 0; o < n; o++) toggles[o].SetValueWithoutNotify(o == c); });
            }
            else if (pd.curve == ParamCurve.Toggle) {
                var t = ZS.Toggle(pd.name + (u.overridden ? " •" : ""), tip, u.value >= 0.5f, v => u.onSet(v ? 1f : 0f), "RichToggle", ZUICornerMask.None, cw, h);
                box.Add(Place(t, 0f, 0f, cw, h));
                refreshers.Add(() => t.SetValueWithoutNotify(read() >= 0.5f));
            }
            else {
                string LabelOf(float v) => pd.name + (u.overridden ? " •" : "") + "  " + G.Format(pd, v);
                ZuiSkinSlider s;
                if (pd.curve == ParamCurve.Logarithmic) {
                    float lmin = Mathf.Log(Mathf.Max(pd.min, 1e-4f)), lmax = Mathf.Log(Mathf.Max(pd.max, 1e-4f));
                    float T(float v) => Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(v, 1e-4f)));
                    ZuiSkinSlider sl = null;
                    sl = ZS.Slider(LabelOf(u.value), T(u.value), 0f, 1f, tip, nt => { float v = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)); u.onDrag(v); sl.text = LabelOf(v); },
                                   ZuiSkinSlider.LabelMode.LabelOnly, T(pd.def), "Default", cw, h);
                    s = sl;
                    refreshers.Add(() => { float v = read(); sl.SetValueWithoutNotify(T(v)); sl.text = LabelOf(v); });
                }
                else {
                    ZuiSkinSlider sl = null;
                    sl = ZS.Slider(LabelOf(u.value), u.value, pd.min, pd.max, tip, nv => {
                        if (pd.curve == ParamCurve.Integer) nv = Mathf.Round(nv);
                        u.onDrag(nv); sl.text = LabelOf(nv);
                    }, ZuiSkinSlider.LabelMode.LabelOnly, pd.def, "Default", cw, h);
                    s = sl;
                    refreshers.Add(() => { float v = read(); sl.SetValueWithoutNotify(v); sl.text = LabelOf(v); });
                }
                box.Add(Place(s, 0f, 0f, cw, h));
                if (u.bound && isEffect) LiveOverlay(s, pd, u.nodeIndex, u.paramIndex, read);
            }

            float right = cw;
            if (u.bound) {
                var tag = Place(Text("~", "Modulated by " + G.BoundBy(chain, u.nodeIndex, u.paramIndex) + ". The slider sets where the modifier starts from; the thin line marks it, and the fill shows where the engine has it right now.", "zs-greymini"),
                                right, -y, G.TagIconW, G.RowH);
                box.Add(tag);
                right += G.TagIconW;
            }
            if (u.warning != null) {
                box.Add(Place(Text("⚠", u.warning + ". " + u.warningTip, "zs-warnmark"), right, -y, G.WarnIconW, G.RowH));
            }

            box.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                G.ShowParamMenu(zound, ZoundDspPlayback.ResolveChain(zound, out _), u.nodeIndex, u.paramIndex, pd, u.overridden);
                e.StopPropagation();
                schedule.Execute(Tick);
            }, TrickleDown.TrickleDown);
            return box;
        }

        /// <summary>
        /// On a modulated slider: the authored value as a thin dark-and-bright marker, and — while a voice plays — the stretch
        /// between it and the engine's live value, brighter where the engine is above it and dimmed back where it is below.
        /// Colours and insets are the old overlay's.
        /// </summary>
        void LiveOverlay(ZuiSkinSlider s, ParamDesc pd, int nodeIndex, int paramIndex, Func<float> read) {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 1f; layer.style.right = 1f; layer.style.top = 1f; layer.style.bottom = 1f;
            var span = new VisualElement { pickingMode = PickingMode.Ignore };
            span.style.position = Position.Absolute; span.style.top = 0; span.style.bottom = 0;
            var dark = new VisualElement { pickingMode = PickingMode.Ignore };
            dark.style.position = Position.Absolute; dark.style.top = 0; dark.style.bottom = 0; dark.style.width = 1f;
            dark.style.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
            var bright = new VisualElement { pickingMode = PickingMode.Ignore };
            bright.style.position = Position.Absolute; bright.style.top = 0; bright.style.bottom = 0; bright.style.width = 1f;
            bright.style.backgroundColor = new Color(0.99f, 0.99f, 1f, 0.95f);
            layer.Add(span); layer.Add(dark); layer.Add(bright);
            s.Add(layer);
            liveRefreshers.Add(() => {
                float iw = layer.resolvedStyle.width;
                if (float.IsNaN(iw) || iw <= 0f) return;
                float tA = G.Normalised(pd, read());
                float xm = Mathf.Clamp(iw * tA, 1f, iw - 1f);
                dark.style.left = xm - 1f; bright.style.left = xm;
                if (ZoundDspPlayback.TryReadLiveParam(zound, nodeIndex, paramIndex, out float live)) {
                    float tL = G.Normalised(pd, live);
                    if (Mathf.Abs(tL - tA) >= 0.001f) {
                        float xa = iw * tA, xl = iw * tL;
                        span.style.display = DisplayStyle.Flex;
                        span.style.left = Mathf.Min(xa, xl); span.style.width = Mathf.Abs(xl - xa);
                        span.style.backgroundColor = tL > tA ? new Color(0.45f, 0.75f, 1f, 0.5f) : new Color(0.04f, 0.04f, 0.06f, 0.72f);
                        return;
                    }
                }
                span.style.display = DisplayStyle.None;
            });
        }

        // ─────────────────────────── modifiers ───────────────────────────

        VisualElement ModifiersHeader(ZoundEffectChain chain) {
            var r = HRow();
            var l = Text("Modifiers", "Value sources that drive effect parameters and the source stage (pitch, source gain): envelopes over the play, LFOs, a random value per play, or a step list. Bind one from a parameter's right-click menu.",
                         "zs-text-subheader", "zs-subheader");
            l.style.width = 80f;
            r.Add(l);
            r.Add(Flex());
            r.Add(ZS.Button("Add modifier…", "Adds a modifier to the stack; bind it to a parameter from that parameter's right-click menu.", "RichButton", () => {
                var items = new List<ZUI.ZUIMenuItem>();
                for (int t = 0; t < ZoundEffectDescriptors.ModifierTypeCount; t++) {
                    var type = (ZoundModifierType)t;
                    items.Add(ZUI.MenuItem(ZoundEffectDescriptors.GetModifier(type).displayName, () => Modify("add modifier", () => {
                        var ch = ZoundDspPlayback.ResolveChain(zound, out _); ch.modifiers.Add(new ZoundModifier(type)); ch.Touch();
                    })));
                }
                ZUI.ContextMenu(items.ToArray());
            }, ZUICornerMask.All, 100f, G.RowH));
            return r;
        }

        VisualElement SourceStageRow(ZoundEffectChain chain, int k) {
            var pd = ZoundEffectDescriptors.SourceStageParams[k];
            var r = Row();
            r.Add(Place(Text("Source " + pd.name.ToLower(), "Source-stage parameter (applied while reading the sample data, ahead of every effect). Right-click to change its modulation.", "zs-guilabel"),
                        G.GripW + 6f, 0f, G.LabelW, G.RowH));
            r.Add(Place(Text("~ " + G.BoundBy(chain, -1, k), "Modulated by this modifier.", "zs-mini"), G.GripW + 6f + G.LabelW + 4f, 0f, 200f, G.RowH));
            r.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                G.ShowParamMenu(zound, ZoundDspPlayback.ResolveChain(zound, out _), -1, k, pd, false);
                e.StopPropagation();
            });
            return r;
        }

        VisualElement ModifierRow(ZoundEffectChain chain, int m, ZoundModifier mod) {
            var desc = ZoundEffectDescriptors.GetModifier(mod.type);
            bool isFolded = folded.Contains(mod);
            var r = Row();
            r.Add(Fill(new Color(1f, 1f, 1f, 0.04f)));
            var fold = Place(Text(isFolded ? "▸" : "▾", isFolded ? "Expand" : "Collapse", "zs-greymini"), 0f, 0f, G.GripW, G.RowH);
            fold.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                if (isFolded) folded.Remove(mod); else folded.Add(mod);
                e.StopPropagation(); Tick();
            });
            r.Add(fold);
            ZuiToggleButton on = ZS.Toggle("On", mod.enabled ? "Disable: its bindings stop applying." : "Enable this modifier.", mod.enabled,
                                           v => Modify(v ? "enable modifier" : "disable modifier", () => { mod.enabled = v; chain.Touch(); }),
                                           "RichToggle", ZUICornerMask.None, G.OnW, G.RowH - 2f);
            r.Add(Place(on, G.GripW + 2f, 1f, G.OnW, G.RowH - 2f));
            float typeX = G.GripW + 2f + G.OnW + 6f;
            r.Add(Place(Text(desc.displayName, desc.summary, "zs-bold"), typeX, 0f, 62f, G.RowH));
            var nameField = new TextField { value = mod.name, tooltip = "The name shown on the parameters this modifier drives." };
            nameField.AddToClassList("zs-namefield");
            nameField.RegisterValueChangedCallback(e => Modify("rename modifier", () => mod.name = e.newValue));
            r.Add(Place(nameField, typeX + 62f + 2f, 1f, 120f, G.RowH - 2f));
            var targets = Place(Text("", "The parameters this modifier drives.", "zs-mini"), typeX + 62f + 2f + 120f + 6f, 0f, -1f, G.RowH);
            targets.style.right = G.RemoveW + 10f;
            r.Add(targets);
            refreshers.Add(() => {
                var ch = ZoundDspPlayback.ResolveChain(zound, out _);
                int mi = ch.modifiers.IndexOf(mod);
                if (mi >= 0) targets.text = G.TargetsSummary(ch, mi);
                on.SetValueWithoutNotify(mod.enabled);
                if (nameField.focusController?.focusedElement != nameField) nameField.SetValueWithoutNotify(mod.name);
            });
            r.Add(PlaceRight(ZS.Button("×", "Removes this modifier and every binding that uses it.", "RichButton",
                () => Modify("remove modifier", () => { var ch = ZoundDspPlayback.ResolveChain(zound, out _); int mi = ch.modifiers.IndexOf(mod); if (mi >= 0) ch.RemoveModifier(mi); }),
                ZUICornerMask.All, G.RemoveW, G.RowH - 2f), 0f, 1f, G.RemoveW, G.RowH - 2f));
            return r;
        }

        List<G.ParamUnit> ModifierUnits(ZoundEffectChain chain, int m, ZoundModifier mod, ModifierDesc desc) {
            var units = new List<G.ParamUnit>(desc.parameters.Length);
            for (int k = 0; k < desc.parameters.Length; k++) {
                var pd = desc.parameters[k];
                int pk = k;
                if (mod.type == ZoundModifierType.Lfo && k == 5 && (int)mod.p[4] != (int)LfoMode.Random) continue;
                if (mod.type == ZoundModifierType.Step && k == 1 && (int)mod.p[0] != (int)StepTiming.PerInterval) continue;
                if (mod.type == ZoundModifierType.Step && k == 5 && (int)mod.p[0] != (int)StepTiming.PerInterval) continue;
                if (mod.type == ZoundModifierType.Step && k == 3 && (int)mod.p[0] == (int)StepTiming.PerInterval && mod.p[4] < 0.5f) continue;
                if (mod.type == ZoundModifierType.Random && k == 1) continue;
                var u = new G.ParamUnit {
                    pd = pd, nodeIndex = int.MinValue, paramIndex = k, value = mod.p[k],
                    onDrag = v => ModifyContinuous("change modifier parameter", () => { mod.p[pk] = v; chain.Touch(); }),
                    onSet = v => Modify("change modifier parameter", () => { mod.p[pk] = v; chain.Touch(); }),
                };
                if (mod.type == ZoundModifierType.Lfo && k == 2)
                    u.icons = new Texture[] { ZUIWaveIcons.Get(ZUIWave.Sine), ZUIWaveIcons.Get(ZUIWave.Triangle), ZUIWaveIcons.Get(ZUIWave.Saw), ZUIWaveIcons.Get(ZUIWave.Square) };
                if (mod.type == ZoundModifierType.Lfo && k == 4)
                    u.icons = new Texture[] { ZUIWaveIcons.Get(ZUIWave.Sine), ZUIWaveIcons.Get(ZUIWave.Random) };
                if (mod.type == ZoundModifierType.Random && k == 0) {
                    u.pdHigh = desc.parameters[1];
                    u.value = Mathf.Clamp(mod.p[0], pd.min, pd.max);
                    u.valueHigh = Mathf.Clamp(mod.p[1], u.pdHigh.min, u.pdHigh.max);
                    var hp = u.pdHigh;
                    u.readHigh = () => Mathf.Clamp(mod.p[1], hp.min, hp.max);
                    u.onRange = (lo, hi) => ModifyContinuous("change modifier range", () => { mod.p[0] = lo; mod.p[1] = hi; chain.Touch(); });
                }
                if (mod.type == ZoundModifierType.Lfo && k == 1 && (int)mod.p[4] == (int)LfoMode.Oscillate) {
                    EnsureModulation(chain);
                    G.SlowRateWarning(modulation.playSeconds, mod, out u.warning, out u.warningTip);
                }
                u.width = G.UnitWidth(u);
                units.Add(u);
            }
            return units;
        }

        void ModifierBody(ZoundEffectChain chain, int m, ZoundModifier mod, float w) {
            var desc = ZoundEffectDescriptors.GetModifier(mod.type);
            var units = ModifierUnits(chain, m, mod, desc);
            Wrap(chain, units, u => () => u.paramIndex < mod.p.Length ? mod.p[u.paramIndex] : 0f, w, this);
            if (mod.type == ZoundModifierType.Envelope || mod.type == ZoundModifierType.Lfo) Add(CurveGround(chain, mod));
            if (mod.type == ZoundModifierType.Step) Add(StepsRow(chain, mod));
        }

        /// <summary>
        /// The curve's ground (the old DrawCurveBackdrop): a dark field, quarter lines, a caption and end labels, and for an
        /// oscillator's strength curve what the oscillator actually puts out, faintly. The curve itself is drawn on it by
        /// the envelope twin (T-0459); the playhead follows the voice.
        /// </summary>
        VisualElement CurveGround(ZoundEffectChain chain, ZoundModifier mod) {
            bool isLfoRamp = mod.type == ZoundModifierType.Lfo;
            var holder = new VisualElement();
            holder.style.height = 56f; holder.style.flexShrink = 0;
            var ground = new VisualElement();
            ground.style.position = Position.Absolute; ground.style.left = G.GripW + 6f; ground.style.right = 0; ground.style.top = 0; ground.style.bottom = 0;
            ground.style.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
            ground.tooltip = (isLfoRamp
                ? "How strongly this oscillator applies as the sound plays, from its start on the left to its end on the right. It does NOT change the wave's shape — that is the Shape buttons above. Flat at the top means full strength throughout, which is how it starts. Drag points; double-click to add one."
                : "The value this envelope produces across the play, from its start on the left to its end on the right (plus any extra time). Drag points; double-click to add one.")
                + (isLfoRamp ? " Behind the curve, faintly: what the oscillator actually puts out across the play, measured from the engine — the curve is the ceiling that wobble can reach." : "")
                + " While the sound plays, an upright line marks the moment being heard.";
            holder.Add(ground);
            var grid = new Color(1f, 1f, 1f, 0.06f);
            for (int q = 1; q < 4; q++) {
                var h = new VisualElement { pickingMode = PickingMode.Ignore };
                h.style.position = Position.Absolute; h.style.left = 0; h.style.right = 0; h.style.top = Length.Percent(q * 25f); h.style.height = 1f; h.style.backgroundColor = grid;
                var v = new VisualElement { pickingMode = PickingMode.Ignore };
                v.style.position = Position.Absolute; v.style.top = 0; v.style.bottom = 0; v.style.left = Length.Percent(q * 25f); v.style.width = 1f; v.style.backgroundColor = grid;
                ground.Add(h); ground.Add(v);
            }
            if (isLfoRamp) {
                // What the oscillator actually puts out across the play, rectified and faint, one column per device pixel,
                // divided by Amount so the strength curve is exactly its ceiling (the old backdrop's rule, measured from
                // the engine, not re-derived).
                var output = new VisualElement { pickingMode = PickingMode.Ignore };
                output.style.position = Position.Absolute; output.style.left = 0; output.style.right = 0; output.style.top = 0; output.style.bottom = 0;
                output.generateVisualContent += ctx => {
                    var ch = ZoundDspPlayback.ResolveChain(zound, out _);
                    int mi = ch != null ? ch.modifiers.IndexOf(mod) : -1;
                    var outs = modulation.modifierOutput;
                    if (mi < 0 || outs == null || mi >= outs.Length || outs[mi] == null || outs[mi].Length < 2) return;
                    var v = outs[mi];
                    float amount = Mathf.Abs(mod.p[0]);
                    if (amount <= 1e-6f) return;
                    var rect = output.contentRect;
                    float px = 1f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                    int cols = Mathf.Max(1, Mathf.FloorToInt(rect.width / px));
                    var p = ctx.painter2D;
                    p.fillColor = new Color(0.6f, 0.75f, 1f, 0.16f);
                    p.BeginPath();
                    for (int c = 0; c < cols; c++) {
                        float idx = (c + 0.5f) / cols * (v.Length - 1);
                        int i = Mathf.FloorToInt(idx);
                        float sv = i >= v.Length - 1 ? v[v.Length - 1] : Mathf.Lerp(v[i], v[i + 1], idx - i);
                        float h = Mathf.Clamp01(Mathf.Abs(sv) / amount) * rect.height;
                        if (h <= 0f) continue;
                        float x0 = c * px, x1 = x0 + px, y0 = rect.height - h;
                        p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, rect.height)); p.LineTo(new Vector2(x0, rect.height)); p.ClosePath();
                    }
                    p.Fill();
                };
                ground.Add(output);
                int drawnVersion = int.MinValue;
                refreshers.Add(() => { if (modulationVersion != drawnVersion) { drawnVersion = modulationVersion; output.MarkDirtyRepaint(); } });
            }

            // The moment being heard, while the sound plays: an upright line and a dot on the curve (the old playhead).
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.style.position = Position.Absolute; head.style.top = 0; head.style.bottom = 0; head.style.width = 1.5f;
            head.style.backgroundColor = new Color(1f, 1f, 1f, 0.9f);
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.position = Position.Absolute; dot.style.width = 6f; dot.style.height = 6f;
            dot.style.backgroundColor = new Color(1f, 0.85f, 0.35f);
            liveRefreshers.Add(() => {
                bool on = SapVoiceRegistry.TryReadPlayPosition(zound, out float elapsed, out float duration) && duration > 0f;
                head.style.display = dot.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) return;
                var r = ground.contentRect;
                float extra = !isLfoRamp && mod.p != null && mod.p.Length > 0 ? Mathf.Max(0f, mod.p[0]) : 0f;
                float frac = Mathf.Clamp01(elapsed / (duration + extra));
                float x = frac * r.width;
                head.style.left = x - 0.5f;
                if (mod.curve != null) {
                    float y = r.height - Mathf.Clamp01(mod.curve.Evaluate(frac)) * r.height;
                    dot.style.left = x - 3f; dot.style.top = y - 3f;
                }
                else dot.style.display = DisplayStyle.None;
            });

            var baseLine = new VisualElement { pickingMode = PickingMode.Ignore };
            baseLine.style.position = Position.Absolute; baseLine.style.left = 0; baseLine.style.right = 0; baseLine.style.bottom = 0; baseLine.style.height = 1f;
            baseLine.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            ground.Add(baseLine);

            var cap = Place(Text(isLfoRamp ? "strength over the play" : "shape over the play", "", "zs-curvecap"), 9f, 0f, 200f, 13f);
            var top = Text(isLfoRamp ? "full" : "top", "", "zs-curvelabel", "zs-right");
            top.style.position = Position.Absolute; top.style.right = 2f; top.style.top = 0; top.style.width = 60f; top.style.height = 12f;
            var bottom = Text(isLfoRamp ? "none" : "bottom", "", "zs-curvelabel");
            bottom.style.position = Position.Absolute; bottom.style.left = 9f; bottom.style.bottom = 1f; bottom.style.width = 60f; bottom.style.height = 12f;
            var secs = Text("", "", "zs-curvelabel", "zs-right");
            secs.style.position = Position.Absolute; secs.style.right = 2f; secs.style.bottom = 1f; secs.style.width = 60f; secs.style.height = 12f;
            ground.Add(cap); ground.Add(top); ground.Add(bottom); ground.Add(secs);
            ground.Add(head); ground.Add(dot);
            refreshers.Add(() => { EnsureModulation(ZoundDspPlayback.ResolveChain(zound, out _)); secs.text = modulation.playSeconds > 0f ? modulation.playSeconds.ToString("0.00") + " s" : ""; });
            return holder;
        }

        /// <summary>The step list's row (the old DrawSteps): the bars (BandSliders twin, T-0459) with + and − beside them.</summary>
        VisualElement StepsRow(ZoundEffectChain chain, ZoundModifier mod) {
            if (mod.steps == null || mod.steps.Length == 0) mod.steps = new float[] { 1f };
            int n = mod.steps.Length;
            var r = Row(G.StepBandH);
            float w = Width - (G.GripW + 6f);
            float buttonsW = 22f * 2f + G.Gap;
            float barW = Mathf.Min(G.StepBarMaxW, (w - buttonsW) / n);
            var band = Place(new VisualElement(), G.GripW + 6f, 2f, barW * n, G.StepBandH - 4f);
            band.AddToClassList("zs-stepband");
            band.tooltip = "Steps: " + n + (n == 1 ? " value" : " values") + " this modifier steps through, one bar each, played left to right (or shuffled, in round-robin order). Drag a bar up or down to set it, or sweep across several to set them all at once; double-click a bar to put it back on the line.";
            r.Add(band);
            float bx = G.GripW + 6f + barW * n + G.Gap, by = 2f + (G.StepBandH - 4f - (G.RowH - 2f)) * 0.5f;
            var add = ZS.Button("+", n < G.MaxSteps ? "Adds a step at the end, copying the last one." : "A list holds at most " + G.MaxSteps + " steps: round-robin order cannot keep track of more.", "RichButton",
                () => Modify("add step", () => { var s = new List<float>(mod.steps); s.Add(s[s.Count - 1]); mod.steps = s.ToArray(); chain.Touch(); }), ZUICornerMask.Left, 22f, G.RowH - 2f);
            add.SetEnabled(n < G.MaxSteps);
            var rem = ZS.Button("−", n > 1 ? "Removes the last step." : "A list needs at least one step.", "RichButton",
                () => Modify("remove step", () => { var s = new List<float>(mod.steps); s.RemoveAt(s.Count - 1); mod.steps = s.ToArray(); chain.Touch(); }), ZUICornerMask.Right, 22f, G.RowH - 2f);
            rem.SetEnabled(n > 1);
            r.Add(Place(add, bx, by, 22f, G.RowH - 2f));
            r.Add(Place(rem, bx + 22f, by, 22f, G.RowH - 2f));
            return r;
        }

        void Bindings(ZoundEffectChain chain, int modifierIndex) {
            for (int i = 0; i < chain.bindings.Count; i++) {
                var b = chain.bindings[i];
                if (b.modifierIndex != modifierIndex) continue;
                var bb = b;
                var r = Row();
                float lx = G.GripW + 6f;
                r.Add(Place(Text("→ " + G.TargetLabel(chain, b), "The parameter this binding drives.", "zs-mini"), lx, 0f, G.LabelW + 60f, G.RowH));
                float x = lx + G.LabelW + 60f + 4f;
                var currentCombine = ChainModulationCompat.CombineOf(b);
                bool legacyShift = currentCombine == ModulationCombine.ShiftWholeRange;
                if (legacyShift) currentCombine = ModulationCombine.Shift;
                bool scaleWorks = G.ScaleIsMeaningfulFor(chain, b);
                var ops = new ZuiToggleButton[3];
                for (int o = 0; o < 3; o++) {
                    int oi = o;
                    var corner = o == 0 ? ZUICornerMask.Left : o == 2 ? ZUICornerMask.Right : ZUICornerMask.None;
                    bool offered = o != (int)ModulationCombine.Scale || scaleWorks;
                    string tip = offered ? G.combineTips[o]
                               : "Scale does nothing on this parameter: it rests at zero, or can go negative, and multiplying either leaves it where it is or flips its sign.";
                    ZuiToggleButton t = null;
                    t = ZS.Toggle(G.combineLabels[o], tip, (int)currentCombine == o, v => {
                        if ((int)currentCombine == oi) { t.SetValueWithoutNotify(true); return; }
                        var combine = (ModulationCombine)oi;
                        Modify("change how the modulator combines", () => G.WriteBinding(chain, bb, combine, bb.depth));
                    }, "RichToggle", corner, 42f, G.RowH - 2f);
                    t.SetEnabled(offered);
                    ops[o] = t;
                    r.Add(Place(t, x + o * 42f, 1f, 42f, G.RowH - 2f));
                }
                float shownDepth = ChainModulationCompat.DepthOf(b, G.DepthMinOf(chain, b), G.DepthMaxOf(chain, b), G.DepthRatioOf(chain, b));
                string depthTip = currentCombine == ModulationCombine.Shift
                    ? (legacyShift
                        ? "How far this modifier may move the parameter. This binding was made before depth changed meaning and still uses the old one: a share of the parameter's WHOLE range each way, so high values pin it against the ends. Change the depth or the mode and it switches to the current meaning, where one reaches the ends but never pins."
                        : "How far this modifier may move the parameter. Nought: not at all. One: all the way to the ends of its range, never past them. A half: half of the room there is in whichever direction it is being pushed.")
                    : currentCombine == ModulationCombine.Set
                        ? "How much the modifier takes over. Nought: your slider value, unchanged. One: entirely the modifier's value. In between: a blend of the two."
                        : "How much the multiplication applies. Nought: no effect. One: your value times the modifier's output. In between: part of the way.";
                var combineNow = currentCombine;
                var depth = ZS.Slider("Depth", shownDepth, 0f, 1f, depthTip,
                                      nd => ModifyContinuous("change depth", () => G.WriteBinding(chain, bb, combineNow, nd)),
                                      ZuiSkinSlider.LabelMode.LabelAndValue, 0.25f, "Default", 120f, G.RowH - 2f);
                r.Add(Place(depth, x + 134f, 1f, 120f, G.RowH - 2f));
                refreshers.Add(() => depth.SetValueWithoutNotify(ChainModulationCompat.DepthOf(bb, G.DepthMinOf(chain, bb), G.DepthMaxOf(chain, bb), G.DepthRatioOf(chain, bb))));
                r.Add(Place(ZS.Button("×", "Removes this binding.", "RichButton",
                    () => Modify("remove binding", () => { chain.bindings.Remove(bb); chain.Touch(); }), ZUICornerMask.All, G.RemoveW, G.RowH - 2f),
                    x + 134f + 120f + 4f, 1f, G.RemoveW, G.RowH - 2f));
                Add(r);
            }
        }
    }
}
