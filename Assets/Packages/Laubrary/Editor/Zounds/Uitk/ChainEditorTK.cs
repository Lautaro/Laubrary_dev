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

        // Kept across rebuilds, so its open state, view and measurements survive an edit to the chain.
        readonly ChainAnalyserTK analyser;
        // Kept across rebuilds too, so what it is pretending to send survives an edit.
        readonly ZpocTestPanelTK zpocTest;
        // The sound's snapshots (T-0498), kept across rebuilds too.
        readonly SnapshotsRowTK snapshotsRow;

        public ChainEditorTK(Zound zound) {
            this.zound = zound;
            analyser = new ChainAnalyserTK(zound);
            zpocTest = new ZpocTestPanelTK(zound);
            snapshotsRow = new SnapshotsRowTK(zound, (undo, action) => Modify(undo, action));
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
                sb.Append((int)mod.type).Append(mod.enabled ? '+' : '-').Append(folded.Contains(mod) ? 'F' : 'U').Append(mod.HasZpoc ? 'Z' : 'z');
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
            Add(snapshotsRow);
            Add(ErrorRow(chain, linked));
            Add(Nodes(chain, linked, w));
            Add(AddEffectRow(chain));
            Add(VSpace(5f));   // ZUI.RowSpace(0.5f)
            Add(ModifiersHeader(chain));
            Add(OwnValuesRow(chain));
            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m];
                Add(ModifierRow(chain, m, mod));
                if (!folded.Contains(mod)) {
                    ModifierBody(chain, m, mod, w);
                    Bindings(chain, m);
                }
            }
            Add(VSpace(5f));   // ZUI.RowSpace(0.5f), then the ZPOC test panel (only when something is exposed) and the analyser
            Add(zpocTest);
            Add(analyser);
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
                string ids = ZpocIdsOn(chain, u.nodeIndex, u.paramIndex);
                var tag = Place(ids != null
                        ? (VisualElement)BoltMark("Game code can move this, through ZPOC " + ids + " (modulated by " + G.BoundBy(chain, u.nodeIndex, u.paramIndex) + "). The slider sets where it starts from; the thin line marks it. While the sound plays, amber shows where code has it, a tick where it is heading, and a flash each time code sends a new value. Hold the slider to take over by hand; let go to hand it back.")
                        : Text("~", "Modulated by " + G.BoundBy(chain, u.nodeIndex, u.paramIndex) + ". The slider sets where the modifier starts from; the thin line marks it, and the fill shows where the engine has it right now.", "zs-greymini"),
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
            // Where the value IS, drawn over where it was SET (T-0492): one instance as a band from the marker, several as
            // their spread; amber when game code has moved it, the modulator colours otherwise; a tick where code is
            // taking it and a pulse when a new value arrives. Nothing live is drawn while nothing plays (T-0446).
            var overlay = new ZuiLiveOverlay();
            s.Add(overlay);
            var values = new float[32];
            var ticks = new List<float>(32);
            float lastSent = float.NaN;
            bool holding = false, suspended = false;

            // Touch: while the hand holds a slider code is driving, the hand wins -- the code-reachable modifiers on this
            // parameter rest in every playing voice, so what is dragged is what is heard. Letting go hands it back.
            s.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                holding = true;
                var sum = SapVoiceRegistry.ReadLiveParam(zound, nodeIndex, paramIndex, null);
                if (sum.driven) { suspended = true; SuspendCode(nodeIndex, paramIndex, true); }
            }, TrickleDown.TrickleDown);
            void Release() {
                if (!holding) return;
                holding = false;
                if (suspended) { suspended = false; SuspendCode(nodeIndex, paramIndex, false); }
            }
            s.RegisterCallback<PointerUpEvent>(_ => Release(), TrickleDown.TrickleDown);
            s.RegisterCallback<PointerCaptureOutEvent>(_ => Release(), TrickleDown.TrickleDown);

            string baseTip = s.tooltip;
            liveRefreshers.Add(() => {
                overlay.SetAuthored(G.Normalised(pd, read()));
                var sum = SapVoiceRegistry.ReadLiveParam(zound, nodeIndex, paramIndex, values);
                if (sum.count == 0) {
                    overlay.ClearLive(); overlay.ClearSpread(); overlay.ClearTarget(); overlay.SetHollow(false);
                    lastSent = float.NaN;
                    if (s.tooltip != baseTip) s.tooltip = baseTip;
                    return;
                }
                // Provenance, stated where the value is (owner, 2026-09-29): while code drives it, the label carries the
                // heard value after the set one, and the hover text says why it is not the set value.
                if (sum.driven && !suspended) {
                    string heard = sum.count == 1 ? G.Format(pd, values[0]) : G.Format(pd, sum.lo) + "–" + G.Format(pd, sum.hi);
                    s.text = s.text + "  ⚡" + heard;
                    s.tooltip = baseTip + "\n\nRight now game code has it at " + heard + (sum.count > 1 ? " across " + sum.count + " plays" : "") +
                                " instead of the " + G.Format(pd, read()) + " set here. " + ZpocPriority;
                }
                else if (s.tooltip != baseTip) s.tooltip = baseTip;
                var kind = sum.driven || suspended ? ZuiLiveKind.Driven : ZuiLiveKind.Modulated;
                if (sum.count == 1) {
                    overlay.ClearSpread();
                    overlay.SetLive(G.Normalised(pd, values[0]), kind);
                }
                else {
                    overlay.ClearLive();
                    ticks.Clear();
                    for (int i = 0; i < Mathf.Min(sum.count, values.Length); i++) ticks.Add(G.Normalised(pd, values[i]));
                    overlay.SetSpread(G.Normalised(pd, sum.lo), G.Normalised(pd, sum.hi), ticks, kind);
                }
                if (sum.hasTarget && sum.moving && !suspended) overlay.SetTarget(G.Normalised(pd, sum.target));
                else overlay.ClearTarget();
                if (!float.IsNaN(lastSent) && sum.sentSignature != lastSent && !suspended) overlay.Pulse();
                lastSent = sum.sentSignature;
                overlay.SetHollow(suspended);
            });
        }

        /// <summary>A 0..1 value in two decimals without the leading nought (".25", "1.00"), so a range fits a chip.</summary>
        static string Short(float v) => v >= 0.995f ? "1.00" : Mathf.Clamp01(v).ToString(".00");

        static VisualElement BoltMark(string tooltip) => new ZpocBolt { tooltip = tooltip };

        /// <summary>The ZPOC ids of the modifiers bound to one parameter ("'wobble', 'throttle'"), or null when code cannot reach it.</summary>
        static string ZpocIdsOn(ZoundEffectChain chain, int nodeIndex, int paramIndex) {
            string ids = null;
            foreach (var b in chain.bindings) {
                if (b.nodeIndex != nodeIndex || b.paramIndex != paramIndex) continue;
                if (b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) continue;
                var m = chain.modifiers[b.modifierIndex];
                if (!m.HasZpoc && m.type != ZoundModifierType.Code) continue;
                string one = m.HasZpoc ? "'" + m.zpocId + "'" : "(a Code modifier with no id yet)";
                ids = ids == null ? one : ids + ", " + one;
            }
            return ids;
        }

        /// <summary>
        /// Touch override for one parameter: rests (or restores) every code-reachable modifier bound to it in every voice
        /// playing this sound. Restoring re-applies what each play's token resolves, so game code picks up exactly where it
        /// was. Voices only; nothing saved changes.
        /// </summary>
        void SuspendCode(int nodeIndex, int paramIndex, bool suspend) {
            if (suspend) {
                SapVoiceRegistry.RestCodeOn(zound, nodeIndex, paramIndex);
                return;
            }
            var tokens = ZoundEngine.LiveTokens;
            if (tokens == null) return;
            for (int i = 0; i < tokens.Count; i++) if (tokens[i] != null && ReferenceEquals(tokens[i].zound, zound)) tokens[i].RefreshAllZpoc();
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

        // The Zound's own values (T-0493), in the order a listener thinks of them. Drive (the level going into the effects)
        // is the source stage's gain, kept last because it is the one that is about the effects rather than the sound.
        static readonly int[] OwnValueOrder = { SourceStageParam.Volume, SourceStageParam.Pitch, SourceStageParam.Speed, SourceStageParam.Gain };
        const float OwnValueW = 150f;

        /// <summary>
        /// The Zound's own values -- Volume, Pitch, Speed, Drive -- as one row (T-0493): what drives each (~ a modifier,
        /// ⚡ game code) and, while it plays, where each is, with the same overlay as the effect sliders. Right-click one to
        /// add or change what moves it, exactly as on any effect setting. They are shown, not dragged: each one's resting
        /// setting is the play's own (the volume and pitch ranges in the header, drawn per play) or simply x1.
        /// </summary>
        VisualElement OwnValuesRow(ZoundEffectChain chain) {
            var r = HRow();
            r.style.marginBottom = 2f;
            var title = Text("Sound", "The sound's own values, as opposed to its effects: its Volume (after every effect, so fading it fades the tails too), Pitch, Speed (without changing pitch) and Drive (the level going into the effects). Right-click one to make a modifier move it, as on any effect setting. Where each rests is the play's own: the volume and pitch ranges above, drawn per play, or x1.", "zs-guilabel");
            title.style.width = G.GripW + 6f + 44f; title.style.flexShrink = 0; title.style.paddingLeft = G.GripW + 6f;
            r.Add(title);
            foreach (int k in OwnValueOrder) {
                r.Add(OwnValue(chain, k));
                r.Add(VSpaceW(6f));
            }
            return r;
        }

        static VisualElement VSpaceW(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }

        VisualElement OwnValue(ZoundEffectChain chain, int k) {
            var pd = ZoundEffectDescriptors.SourceStageParams[k];
            var box = new VisualElement();
            box.AddToClassList("zs-ownvalue");
            box.style.width = OwnValueW; box.style.height = G.RowH - 2f; box.style.marginTop = 1f; box.style.flexShrink = 0;
            var overlay = new ZuiLiveOverlay();
            box.Add(overlay);
            var text = new Label { pickingMode = PickingMode.Ignore };
            text.AddToClassList("zs-ownvalue__text");
            text.style.position = Position.Absolute; text.style.left = 0; text.style.right = 0; text.style.top = 0; text.style.bottom = 0;
            box.Add(text);
            bool bound = G.IsBound(chain, -1, k);
            string ids = bound ? ZpocIdsOn(chain, -1, k) : null;
            string by = bound ? G.BoundBy(chain, -1, k) : null;
            string mark = !bound ? "" : ids != null ? "  ⚡ " + by : "  ~ " + by;
            box.EnableInClassList("zs-ownvalue--bound", bound);
            box.EnableInClassList("zs-ownvalue--code", ids != null);
            string baseTip = pd.name + ": " + pd.desc + (bound ? " Moved by " + by + (ids != null ? ", which game code reaches as " + ids : "") + "." : " Nothing moves it; right-click to add a modifier.") + " Right-click to change what moves it.";
            box.tooltip = baseTip;
            text.text = pd.name + mark;
            box.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                G.ShowParamMenu(zound, ZoundDspPlayback.ResolveChain(zound, out _), -1, k, pd, false);
                e.StopPropagation();
                schedule.Execute(Tick);
            });
            var values = new float[32];
            var ticks = new List<float>(32);
            liveRefreshers.Add(() => {
                overlay.SetAuthored(G.Normalised(pd, pd.def));
                var sum = SapVoiceRegistry.ReadLiveParam(zound, -1, k, values);
                if (sum.count == 0 || !bound) {
                    overlay.ClearLive(); overlay.ClearSpread();
                    text.text = pd.name + mark;
                    box.tooltip = baseTip;
                    return;
                }
                var kind = sum.driven ? ZuiLiveKind.Driven : ZuiLiveKind.Modulated;
                string heard;
                if (sum.count == 1) { overlay.ClearSpread(); overlay.SetLive(G.Normalised(pd, values[0]), kind); heard = G.Format(pd, values[0]); }
                else {
                    overlay.ClearLive(); ticks.Clear();
                    for (int i = 0; i < Mathf.Min(sum.count, values.Length); i++) ticks.Add(G.Normalised(pd, values[i]));
                    overlay.SetSpread(G.Normalised(pd, sum.lo), G.Normalised(pd, sum.hi), ticks, kind);
                    heard = G.Format(pd, sum.lo) + "–" + G.Format(pd, sum.hi);
                }
                text.text = pd.name + " " + heard + (ids != null ? "  ⚡" : "  ~");
                box.tooltip = baseTip + (sum.driven ? "\n\nRight now game code has it at " + heard + ". " + ZpocPriority : "");
            });
            return box;
        }

        VisualElement SourceStageRow(ZoundEffectChain chain, int k) {
            var pd = ZoundEffectDescriptors.SourceStageParams[k];
            var r = Row();
            r.Add(Place(Text("Source " + pd.name.ToLower(), "Source-stage parameter (applied while reading the sample data, ahead of every effect). Right-click to change its modulation.", "zs-guilabel"),
                        G.GripW + 6f, 0f, G.LabelW, G.RowH));
            string ids = ZpocIdsOn(chain, -1, k);
            r.Add(Place(ids != null
                    ? Text("⚡ " + G.BoundBy(chain, -1, k), "Game code can move this, through ZPOC " + ids + ".", "zs-mini", "zs-zpoctext")
                    : Text("~ " + G.BoundBy(chain, -1, k), "Modulated by this modifier.", "zs-mini"),
                G.GripW + 6f + G.LabelW + 4f, 0f, 200f, G.RowH));
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
            float chipX = typeX + 62f + 2f + 120f + 6f;
            r.Add(Place(ZpocChip(mod), chipX, 1f, ZpocChipW, G.RowH - 2f));
            var targets = Place(Text("", "The parameters this modifier drives.", "zs-mini"), chipX + ZpocChipW + 6f, 0f, -1f, G.RowH);
            targets.style.right = G.RemoveW + 10f + EyeW + 4f;
            // The eye (T-0494): whether this modifier is counted in the waveform's combined-result lines and drawn there.
            r.Add(PlaceRight(ZS.Eye(CurveView.IsVisible(mod), v => v
                    ? "Shown: what this modifier does is included in the combined-result lines on the waveform (and its curve drawn there). Click to leave it out of the picture; it keeps playing."
                    : "Hidden from the waveform's pictures: its effect is left out of the combined-result lines and its curve is not drawn there. It still plays. Click to show it.",
                v => { CurveView.SetVisible(mod, v); }, ZUICornerMask.All, EyeW, G.RowH - 2f), G.RemoveW + 6f, 1f, EyeW, G.RowH - 2f));
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

        const float ZpocChipW = 150f;
        const float EyeW = 22f;

        /// <summary>The precedence order, stated wherever a code-driven value is shown (owner, 2026-09-29: "important that
        /// it's clearly stated no matter what the order is").</summary>
        internal const string ZpocPriority =
            "Which value wins, highest first: one set on this play (or on the Zequence playing it), then a project-wide one, " +
            "then where the ZPOC rests (the snapshot's value, else as authored). Modifiers then move the parameter from there.";

        /// <summary>Where the value of one ZPOC comes from across every play of this sound: "play", "global", "rest", or
        /// "mixed" when plays disagree. Empty when nothing plays.</summary>
        string ZpocSourceOf(string id) {
            var tokens = ZoundEngine.LiveTokens;
            if (tokens == null) return "";
            int seen = -1;
            for (int i = 0; i < tokens.Count; i++) {
                var t = tokens[i];
                if (t == null || !ReferenceEquals(t.zound, zound) || t.state == ZoundToken.State.Killed) continue;
                int src = (int)t.SourceOfZpoc(id);
                if (src == (int)ZoundToken.ZpocSource.Parent) src = (int)ZoundToken.ZpocSource.Play;
                if (seen == -1) seen = src; else if (seen != src) return "mixed";
            }
            return seen == (int)ZoundToken.ZpocSource.Play ? "play" : seen == (int)ZoundToken.ZpocSource.Global ? "global"
                 : seen == (int)ZoundToken.ZpocSource.Rest ? "rest" : "";
        }

        /// <summary>
        /// A modifier's ZPOC chip (T-0492): grey "⚡" when game code cannot reach it, amber "⚡ id" when it can. While the
        /// sound plays, its fill shows the value code has it at (the spread across instances when several play) and it
        /// flashes when a new value arrives. Click to set the id and how the value acts.
        /// </summary>
        VisualElement ZpocChip(ZoundModifier mod) {
            // A label, not a box (owner, 2026-09-30, T-0514): with no id only a dull bolt; with one, the lit bolt and the id as
            // plain text. While the sound plays, where code has it shows as a thin meter under the label.
            var chip = new VisualElement();
            chip.AddToClassList("zs-zpocchip");
            chip.style.flexDirection = FlexDirection.Row; chip.style.alignItems = Align.Center;
            var bolt = new ZpocBolt { pickingMode = PickingMode.Ignore };
            bolt.style.width = 12f; bolt.style.height = 14f; bolt.style.flexShrink = 0;
            var spread = new VisualElement { pickingMode = PickingMode.Ignore }; spread.AddToClassList("zs-zpocchip__spread");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("zs-zpocchip__fill");
            var text = new Label { pickingMode = PickingMode.Ignore }; text.AddToClassList("zs-zpocchip__text");
            text.style.flexGrow = 1; text.style.flexShrink = 1;
            chip.Add(bolt); chip.Add(text); chip.Add(spread); chip.Add(fill);
            chip.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 && e.button != 1) return;
                ZpocPopup.Show(chip.worldBound, zound, mod, () => { ZoundDspPlayback.InvalidateLayout(zound); schedule.Execute(Tick); });
                e.StopPropagation();
            });
            var values = new float[32];
            float lastSent = float.NaN, pulseUntil = 0f;
            string Src(ZoundModifier m) { var src = m.HasZpoc ? ZpocSourceOf(m.zpocId) : ""; return src.Length > 0 ? " · " + src : ""; }
            refreshers.Add(() => {
                bool code = mod.type == ZoundModifierType.Code;
                bool on = mod.HasZpoc;
                chip.EnableInClassList("zs-zpocchip--on", on);
                bolt.Color = on ? ZpocBolt.Amber : ZpocBolt.Unlit;
                chip.tooltip = on
                    ? "Game code reaches this modifier as '" + mod.zpocId + "': token.SetZpoc(\"" + mod.zpocId + "\", value). " +
                      (code ? "Its output IS that value. " : mod.zpocMode == ZpocMode.Scale ? "Scale: the value scales how strongly it acts, as authored. " : "Set: the value is how strongly it acts. ") +
                      "While the sound plays, the value follows the id, the line underneath shows where code has it, and it brightens when a new value arrives; the word after the value says where that value comes from (play, global or rest). " + ZpocPriority + " Click to change."
                    : (code ? "This Code modifier has no id yet, so game code cannot reach it. Click to give it one."
                            : "Game code cannot reach this modifier. Click to give it a ZPOC id, so code can turn it up and down while the sound plays.");
            });
            liveRefreshers.Add(() => {
                var ch = ZoundDspPlayback.ResolveChain(zound, out _);
                int mi = ch != null ? ch.modifiers.IndexOf(mod) : -1;
                string name = mod.HasZpoc ? mod.zpocId : "";
                var sum = mi >= 0 && (mod.HasZpoc || mod.type == ZoundModifierType.Code) ? SapVoiceRegistry.ReadModifierControl(zound, mi, values) : default;
                if (sum.count == 0) {
                    text.text = name; fill.style.display = DisplayStyle.None; spread.style.display = DisplayStyle.None;
                    lastSent = float.NaN;
                }
                else {
                    // A Code modifier's control is its value; any other's is the multiplier on its depths, shown as a share.
                    float w = chip.resolvedStyle.width; if (float.IsNaN(w)) w = ZpocChipW;
                    w -= 14f;   // the meter runs under the label, after the bolt
                    float lo = Mathf.Clamp01(sum.lo), hi = Mathf.Clamp01(sum.hi);
                    fill.style.display = sum.count == 1 ? DisplayStyle.Flex : DisplayStyle.None;
                    fill.style.left = 14f; fill.style.width = w * lo;
                    if (sum.count > 1) {
                        spread.style.display = DisplayStyle.Flex; spread.style.left = 14f + w * lo; spread.style.width = Mathf.Max(1f, w * (hi - lo));
                        text.text = name + " " + Short(sum.lo) + "–" + Short(sum.hi) + " ×" + sum.count + Src(mod);
                    }
                    else {
                        spread.style.display = DisplayStyle.None;
                        text.text = name + " " + Short(sum.lo) + Src(mod);
                    }
                    if (!float.IsNaN(lastSent) && sum.sentSignature != lastSent) pulseUntil = Time.realtimeSinceStartup + ZuiLiveOverlay.PulseSeconds;
                    lastSent = sum.sentSignature;
                }
                chip.EnableInClassList("zs-zpocchip--pulse", Time.realtimeSinceStartup < pulseUntil);
            });
            return chip;
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
                // An envelope measured against the waveform (its default) is read at the source position, so its playhead
                // is too (T-0493): the clock drifts from it as soon as pitch or speed change how fast the source goes by.
                bool waveformBase = !isLfoRamp && (mod.p == null || mod.p.Length < 2 || mod.p[1] < 0.5f);
                if (waveformBase && SapVoiceRegistry.TryReadSourceProgress(zound, out float prog, out float region) && region > 0f && prog < 0.999f)
                    frac = Mathf.Clamp01(prog * region / (region + extra));
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
            // What the curve's top, middle and bottom mean for what it drives (T-0479): semitones on a pitch curve, ratios
            // on another Ratio curve, the parameter's own ends under Set. A centre line marks "no change" where there is one.
            var mid = Text("", "", "zs-curvelabel");
            mid.style.position = Position.Absolute; mid.style.left = 9f; mid.style.width = 60f; mid.style.height = 12f;
            var midLine = new VisualElement { pickingMode = PickingMode.Ignore };
            midLine.style.position = Position.Absolute; midLine.style.left = 0; midLine.style.right = 0; midLine.style.height = 1f;
            midLine.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f);
            void Axis() {
                if (isLfoRamp) return;
                KlipChainEnvelopes.CurveAxis(ZoundDspPlayback.ResolveChain(zound, out _), mod, out string t, out string mText, out string bText);
                top.text = t; bottom.text = bText;
                bool hasMid = mText != null;
                mid.style.display = midLine.style.display = hasMid ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasMid) {
                    mid.text = mText;
                    float h = ground.contentRect.height;
                    if (h > 1f) { midLine.style.top = Mathf.Round(h * 0.5f); mid.style.top = Mathf.Round(h * 0.5f) - 13f; }
                }
            }
            ground.RegisterCallback<GeometryChangedEvent>(_ => Axis());
            refreshers.Add(Axis);
            ground.Add(midLine); ground.Add(mid);
            ground.Add(cap); ground.Add(top); ground.Add(bottom); ground.Add(secs);

            // The curve itself, on top of the ground and under the playhead, as the old editor paints them.
            if (mod.curve == null) mod.curve = new Envelope(0f, 1f);
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            var curve = new EnvelopeTK(mod.curve, mod.type == ZoundModifierType.Envelope ? es.volumeEnvelopeColor : es.pitchEnvelopeColor);
            curve.style.position = Position.Absolute; curve.style.left = 0; curve.style.right = 0; curve.style.top = 0; curve.style.bottom = 0;
            curve.tooltip = ground.tooltip;
            curve.onBegin = () => {
                if (dragUndoOpen) return;
                dragUndoOpen = true;
                ZoundsWindow.BeginDragUndo("edit modifier curve");
                // Editing the Klip's pitch curve moves it off the old scale first, sounding the same (T-0479).
                KlipChainEnvelopes.EnsurePitchRatioIfPitchCurve(zound, mod);
            };
            curve.onChanged = () => {
                ZoundDspPlayback.ResolveChain(zound, out _)?.Touch();
                ZoundDspPlayback.InvalidateLayout(zound);
                EditorUtility.SetDirty(ZoundsProject.Instance);
            };
            // What the plays under way hear, dotted over the curve (T-0484); follows plays starting and stopping.
            int liveShown = 0;
            curve.schedule.Execute(() => {
                var ch = ZoundDspPlayback.ResolveChain(zound, out _);
                int n = LiveDrawnCurves.Fill(ref curve.liveCurves, zound, mod.curve, ch != null ? ch.modifiers.IndexOf(mod) : -1);
                if (n > 0 || liveShown > 0) curve.Refresh();
                liveShown = n;
            }).Every(50);
            // Right-click a point: its random settings (T-0483).
            curve.onPointContext = (i, world) => {
                if (mod.curve == null || i < 0 || i >= mod.curve.Count) return;
                var sel = new List<ZUIEnvelopePoint>();
                foreach (int s in curve.SelectedPoints) if (s >= 0 && s < mod.curve.Count) sel.Add(mod.curve.GetPoint(s));
                RandomPointPopup.Show(world, mod.curve.GetPoint(i), Mathf.Max(mod.curve.xMax - mod.curve.xMin, 1e-3f),
                    () => mod.curve.yMax - mod.curve.yMin,
                    () => KlipChainEnvelopes.EnsurePitchRatioIfPitchCurve(zound, mod),
                    () => { curve.onChanged?.Invoke(); curve.Refresh(); },
                    sel,
                    () => KlipChainEnvelopes.NeutralValue(ZoundDspPlayback.ResolveChain(zound, out _), mod, out float v) ? v : (float?)null,
                    () => new Vector2(mod.curve.yMin, mod.curve.yMax));
            };
            ground.Add(curve);
            refreshers.Add(() => { if (curve.envelope != mod.curve) curve.envelope = mod.curve; curve.Refresh(); });
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
            // What "no change" is depends on how the list is bound (the old editor's rule, T-0433): when every binding of
            // it scales, the bars run from nought to two about a line at one; otherwise from -1 to +1 about nought.
            int mi = chain.modifiers.IndexOf(mod);
            bool anyBinding = false, allScale = true;
            foreach (var b in chain.bindings) {
                if (b.modifierIndex != mi) continue;
                anyBinding = true;
                if (ChainModulationCompat.CombineOf(b) != ModulationCombine.Scale) allScale = false;
            }
            bool scaling = anyBinding && allScale;
            float lo = scaling ? 0f : -1f, hi = scaling ? 2f : 1f, rest = scaling ? 1f : 0f;
            var band = Place(new ZuiSkinBandSliders(mod.steps, lo, hi, rest,
                edited => ModifyContinuous("change step value", () => { mod.steps = edited; chain.Touch(); }), rest,
                i => i < mod.steps.Length
                    ? "Step " + (i + 1) + ": " + (scaling ? "×" + mod.steps[i].ToString("0.00") : mod.steps[i].ToString("+0.00;-0.00;0.00"))
                      + (scaling ? "  (this list scales what it is bound to: the line is ×1, unchanged; the bottom is ×0" + (mod.steps[i] < 0f ? "; below zero pins the parameter at its minimum" : "") + ")"
                                 : "  (the line is no change; top and bottom are the furthest this list moves what it is bound to)")
                      + (mod.steps[i] > hi || mod.steps[i] < lo ? ". Beyond the bars' range; dragging it brings it back inside." : "")
                    : null), G.GripW + 6f, 2f, barW * n, G.StepBandH - 4f);
            band.AddToClassList("zs-slider-default");
            refreshers.Add(() => band.SetValues(mod.steps));
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
                bool ratioWorks = G.RatioOfferedFor(chain, b);
                var ops = new ZuiToggleButton[4];
                for (int o = 0; o < 4; o++) {
                    int oi = o;
                    var corner = o == 0 ? ZUICornerMask.Left : o == 3 ? ZUICornerMask.Right : ZUICornerMask.None;
                    bool offered = o == 2 ? scaleWorks : o == 3 ? ratioWorks : true;
                    string tip = offered ? G.combineTips[o]
                               : o == 3 ? G.RatioNotOfferedTip
                               : "Scale does nothing on this parameter: it rests at zero, or can go negative, and multiplying either leaves it where it is or flips its sign.";
                    ZuiToggleButton t = null;
                    t = ZS.Toggle(G.combineLabels[o], tip, G.OptionOf(currentCombine) == o, v => {
                        if (G.OptionOf(currentCombine) == oi) { t.SetValueWithoutNotify(true); return; }
                        var combine = G.OptionCombine(oi);
                        Modify("change how the modulator combines", () => G.ChooseCombine(chain, bb, combine));
                    }, "RichToggle", corner, 42f, G.RowH - 2f);
                    t.SetEnabled(offered);
                    ops[o] = t;
                    r.Add(Place(t, x + o * 42f, 1f, 42f, G.RowH - 2f));
                }
                float shownDepth = ChainModulationCompat.DepthOf(b, G.DepthMinOf(chain, b), G.DepthMaxOf(chain, b), G.DepthRatioOf(chain, b));
                string depthTip = G.DepthTip(currentCombine, legacyShift);
                var combineNow = currentCombine;
                var depth = ZS.Slider("Depth", shownDepth, 0f, 1f, depthTip,
                                      nd => ModifyContinuous("change depth", () => G.WriteBinding(chain, bb, combineNow, nd)),
                                      ZuiSkinSlider.LabelMode.LabelAndValue, 0.25f, "Default", 120f, G.RowH - 2f);
                r.Add(Place(depth, x + 176f, 1f, 120f, G.RowH - 2f));
                refreshers.Add(() => depth.SetValueWithoutNotify(ChainModulationCompat.DepthOf(bb, G.DepthMinOf(chain, bb), G.DepthMaxOf(chain, bb), G.DepthRatioOf(chain, bb))));
                r.Add(Place(ZS.Button("×", "Removes this binding.", "RichButton",
                    () => Modify("remove binding", () => { chain.bindings.Remove(bb); chain.Touch(); }), ZUICornerMask.All, G.RemoveW, G.RowH - 2f),
                    x + 176f + 120f + 4f, 1f, G.RemoveW, G.RowH - 2f));
                Add(r);
            }
        }
    }
}
