using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// The per-Zound effect chain editor: an ordered, drag-reorderable list of effect nodes, each with a
    /// parameter panel generated from <see cref="ZoundEffectDescriptors"/>, plus the modifier stack and
    /// its bindings, plus the chain-library bar (link, detach, reconnect, save as preset).
    /// Hosted by the Klip and Zequence editor windows; IMGUI/ZUI like the rest of the Zounds windows.
    ///
    /// A zound linked to a preset edits the PRESET (every user hears it); dragging a parameter on a
    /// linked zound sets a per-zound override instead (marked, revertable from its context menu).
    /// </summary>
    internal class ChainEditorGUI {

        internal const float RowH = 20f;
        internal const float GripW = 14f;
        internal const float OnW = 30f;
        internal const float RemoveW = 20f;
        internal const float LabelW = 88f;
        /// <summary>
        /// The effect-name column, as wide as the longest effect name and no wider — measured, because a fixed guess clipped
        /// "Transient shaper" to "Transient shape" (seen on screen). Every row shares it, so the controls start in one column.
        /// </summary>
        internal static float NameW {
            get {
                if (nameW > 0f) return nameW;
                float w = 60f;
                for (int t = 0; t < ZoundEffectDescriptors.EffectTypeCount; t++)
                    w = Mathf.Max(w, EditorStyles.boldLabel.CalcSize(new GUIContent(ZoundEffectDescriptors.Get((ZoundEffectType)t).displayName)).x + 6f);
                return nameW = w;
            }
        }
        private static float nameW;

        // ── the wide-not-tall layout (T-0437) ──
        // The chain editor is used WIDE: horizontal room is cheap and vertical room is the scarce thing. So every setting
        // is a self-labelled unit of known width, and units flow left to right, starting a new row only when the next one
        // does not fit. A slider carries its name and value inside its own track; a toggle's face is its name; a choice
        // strip speaks through its options (the setting's name is on hover); a choice between shapes is icons. There is no
        // separate label column any more — that column is what forced one setting per row.
        internal const float Gap = 6f;
        internal const float SliderUnitW = 158f;
        internal const float TagIconW = 14f;
        internal const float WarnIconW = 16f;

        /// <summary>
        /// How much narrower the rows really are than the window, measured on a paint pass and used on every pass after.
        ///
        /// Wrapping has to be decided identically in IMGUI's layout pass and its paint pass, and only the paint pass knows
        /// the real width of a row. So the difference is remembered from the paint pass and the window's own width, which
        /// both passes know, is used with it — a row decision can then never differ between the two passes.
        /// </summary>
        private float widthDelta = 40f;
        private float AvailableWidth => Mathf.Max(200f, EditorGUIUtility.currentViewWidth - widthDelta);
        private float HeaderLeadW => GripW + 2f + OnW + 6f + NameW + Gap;

        private int selectedNode = -1;
        /// <summary>Which effect's settings are expanded (-1: none). Set from outside only by the UI Toolkit comparison gallery.</summary>
        internal int SelectedNode { get => selectedNode; set => selectedNode = value; }
        private int dragNode = -1;
        private int dragTargetIndex = -1;
        private bool dragUndoOpen;
        private readonly Dictionary<ZoundModifier, EnvelopeGUI> envelopeGuis = new Dictionary<ZoundModifier, EnvelopeGUI>();
        private readonly HashSet<ZoundModifier> foldedModifiers = new HashSet<ZoundModifier>();
        private readonly List<Rect> nodeRowRects = new List<Rect>();
        private static readonly GUIContent tmp = new GUIContent();

        public bool isDragging => dragUndoOpen || dragNode >= 0;

        readonly EditorTools.ChainAnalyserPanel analyser = new EditorTools.ChainAnalyserPanel();

        /// <summary>Releases what the embedded analyser holds natively. The hosting window calls this when it closes.</summary>
        public void Dispose() => analyser.Dispose();

        /// <summary>
        /// True while something on screen is moving on its own and the hosting window must keep repainting: a live analyser
        /// view, or a modulated parameter whose engine value is being tracked. Both switch themselves off — the overlay only
        /// reports movement while a sound is actually playing — so an idle editor costs nothing.
        /// </summary>
        public bool wantsContinuousRepaint => analyser.wantsContinuousRepaint || liveParamsAnimating
                                              || Dsp.SapVoiceRegistry.IsPlaying(drawnZound);

        /// <summary>
        /// The sound drawn most recently, so the hosting window can be told to keep redrawing the moment it STARTS playing.
        ///
        /// Without this the editor could only learn a sound was playing from its own previous drawing — and the drawing
        /// right after Play is pressed happens before the sound has actually begun, so it saw nothing, decided nothing was
        /// moving, and stopped redrawing. The live fill on a modulated slider was then never drawn at all, which is exactly
        /// the "reported done, nothing on screen" the owner hit. Asking the engine each tick removes the chicken-and-egg.
        /// </summary>
        private Zound drawnZound;

        // What every modifier outputs across one play, measured from the engine and cached against the chain's revision, so
        // a modifier's own graph can show what it actually produces. Cheap (one short control-rate render), and re-taken
        // only after edits settle for a moment, the same way the analyser does.
        private EditorTools.ChainSpectrumProbe.Measurement modulation;
        private int modulationVersion = int.MinValue, modulationPending = int.MinValue;
        private double modulationPendingSince;
        private Zound modulationZound;

        private void EnsureModulationMeasured(Zound zound, ZoundEffectChain chain) {
            if (chain == null) return;
            if (modulationVersion == chain.version && ReferenceEquals(modulationZound, zound)) return;
            double now = EditorApplication.timeSinceStartup;
            if (modulationPending != chain.version) { modulationPending = chain.version; modulationPendingSince = now; }
            if (now - modulationPendingSince < 0.15 && modulation.modifierOutput != null) return;
            modulationVersion = chain.version;
            modulationZound = zound;
            if (!Dsp.ZoundSapPlayback.TryGetPlayLength(zound, out float play)) play = 1.5f;
            modulation = EditorTools.ChainSpectrumProbe.MeasureModulation(chain, play);
        }

        // ───────────────────────────── entry ─────────────────────────────

        public void Draw(Zound zound) {
            var chain = ZoundDspPlayback.ResolveChain(zound, out var preset);
            bool linked = preset != null;
            drawnZound = zound;
            var evt = Event.current;
            if (evt.type == EventType.Repaint) {
                ZoundsEditorDiagnostics.chainEditorRepaints++;
                ZoundsEditorDiagnostics.BeginChainEditor(new Rect(0, 0, EditorGUIUtility.currentViewWidth, 0));
                // Cleared only on a repaint, and before the rows that set it, so it always describes the most recent frame
                // that actually drew. Clearing it on layout passes too would drop the flag between frames and stall the
                // animation the moment it started.
                liveParamsAnimating = false;
            }

            DrawLibraryBar(zound, chain, preset);

            // A reserved single line rather than a help box that comes and goes.
            //
            // This check runs on every repaint against the current parameter values, so an error can appear and vanish
            // mid-drag — and a help box appearing mid-drag shoves the entire effect list, the modifiers and the analyser
            // down by two rows, then yanks them back up. The row is therefore always there, the message is clipped into it
            // with the full text on hover, and the warning reads as a warning through its colour and marker instead of
            // through taking up space it only sometimes needs.
            var layoutError = ChainLayout.Build(chain, AudioSettings.outputSampleRate, linked ? zound.chainOverrides : null).error;
            var errorRow = GUILayoutUtility.GetRect(10f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (layoutError != null) {
                EditorGUI.DrawRect(new Rect(errorRow.x, errorRow.y, 3f, errorRow.height), new Color(0.95f, 0.75f, 0.3f));
                var warn = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
                warn.normal.textColor = new Color(0.97f, 0.8f, 0.4f);
                GUI.Label(new Rect(errorRow.x + 7f, errorRow.y, errorRow.width - 7f, errorRow.height),
                          new GUIContent("⚠ " + layoutError, layoutError), warn);
            }

            DrawNodes(zound, chain, linked);
            ZUI.RowSpace(0.5f);
            DrawModifiers(zound, chain, linked);

            // The analyser lives HERE rather than in a window of its own, and that is the whole fix. As a separate window
            // it had to be told which sound to look at, which meant it was usually looking at the wrong one — and it could
            // not see a sound nested inside a sequence at all. Underneath the effects it describes, there is nothing to
            // point it at: it is always the chain in front of you, and it refreshes as you edit because the chain's own
            // revision counter tells it something changed.
            ZUI.RowSpace(0.5f);
            analyser.Draw(zound, chain);

            if (evt.rawType == EventType.MouseUp || evt.rawType == EventType.MouseLeaveWindow) {
                if (dragUndoOpen) { dragUndoOpen = false; ZoundsWindow.EndDragUndo(); }
                if (dragNode >= 0) FinishNodeDrag(zound, chain);
            }
        }

        internal static void Modify(Zound zound, string undo, System.Action action) {
            ZoundsWindow.ModifyAndSaveZoundsProject(undo, () => {
                action();
                ZoundDspPlayback.InvalidateLayout(zound);
            });
        }

        // Continuous edits (slider drags) open one undo group on the first change and close it on mouse up.
        private void ModifyContinuous(Zound zound, string undo, System.Action action) {
            if (!dragUndoOpen) { dragUndoOpen = true; ZoundsWindow.BeginDragUndo(undo); }
            action();
            ZoundDspPlayback.InvalidateLayout(zound);
            EditorUtility.SetDirty(ZoundsProject.Instance);
        }

        // ───────────────────────────── library bar ─────────────────────────────

        private void DrawLibraryBar(Zound zound, ZoundEffectChain chain, ZoundChainPreset preset) {
            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            {
                string title = preset != null ? preset.name : "Local chain";
                int users = preset != null ? ZoundChainLibrary.CountUsers(preset.id) : 0;
                tmp.text = preset != null ? title + "  (" + users + (users == 1 ? " user)" : " users)") : title;
                tmp.tooltip = preset != null
                    ? "This zound plays the library preset '" + preset.name + "' by live reference: editing the nodes below changes every zound using it."
                    : "This zound's own chain. Save it as a preset to share it with other zounds.";
                ZUI.Label(tmp.text, ZUI.ZTextStyle.Subheader, GUILayout.Width(220f));
                var last = GUILayoutUtility.GetLastRect();
                GUI.Label(last, new GUIContent("", tmp.tooltip));

                GUILayout.FlexibleSpace();

                tmp.text = "Library…"; tmp.tooltip = "Browse the chain presets: use one on this zound, audition it, rename, duplicate or delete presets.";
                if (ZUI.Button(tmp, ZUI.Style.RichButton, ZUICornerMask.Left, GUILayout.Width(70f), GUILayout.Height(RowH))) {
                    ChainLibraryPopup.Show(GUILayoutUtility.GetLastRect(), zound);
                }
                tmp.text = "Save as…"; tmp.tooltip = "Saves a copy of this chain as a new library preset and links this zound to it.";
                if (ZUI.Button(tmp, ZUI.Style.RichButton, ZUICornerMask.None, GUILayout.Width(70f), GUILayout.Height(RowH))) {
                    SavePresetPopup.Show(GUILayoutUtility.GetLastRect(), zound.name + " chain", name => {
                        Modify(zound, "save chain as preset", () => {
                            var p = ZoundChainLibrary.Create(name, chain);
                            ZoundChainLibrary.Assign(zound, p);
                        });
                    });
                }
                if (preset != null) {
                    tmp.text = "Detach"; tmp.tooltip = "Breaks the link: this zound keeps a private copy of the preset (with its overrides folded in) and stops following preset edits.";
                    if (ZUI.Button(tmp, ZUI.Style.RichButton, ZUICornerMask.Right, GUILayout.Width(62f), GUILayout.Height(RowH))) {
                        Modify(zound, "detach chain preset", () => ZoundChainLibrary.Detach(zound));
                    }
                }
                else {
                    bool canReconnect = ZoundChainLibrary.CanReconnect(zound);
                    var prev = GUI.enabled;
                    GUI.enabled = prev && canReconnect;
                    var rp = ZoundChainLibrary.Find(zound.detachedChainPresetId);
                    tmp.text = "Reconnect"; tmp.tooltip = canReconnect ? "Follows the preset '" + rp.name + "' again; this zound's private copy is discarded." : "Nothing to reconnect to: this chain was not detached from a preset.";
                    if (ZUI.Button(tmp, ZUI.Style.RichButton, ZUICornerMask.Right, GUILayout.Width(78f), GUILayout.Height(RowH))) {
                        Modify(zound, "reconnect chain preset", () => ZoundChainLibrary.Reconnect(zound));
                    }
                    GUI.enabled = prev;
                }
            }
            GUILayout.EndHorizontal();
        }

        // ───────────────────────────── nodes ─────────────────────────────

        private void DrawNodes(Zound zound, ZoundEffectChain chain, bool linked) {
            var evt = Event.current;
            nodeRowRects.Clear();
            if (chain.nodes.Count == 0) {
                ZUI.Label("No effects.", ZUI.ZTextStyle.Subtle);
            }
            for (int i = 0; i < chain.nodes.Count; i++) {
                var node = chain.nodes[i];
                node.EnsureParams();
                var desc = ZoundEffectDescriptors.Get(node.type);
                var row = GUILayoutUtility.GetRect(1f, RowH, GUILayout.ExpandWidth(true));
                if (evt.type == EventType.Repaint && row.width > 50f)
                    widthDelta = Mathf.Clamp(EditorGUIUtility.currentViewWidth - row.width, 0f, 400f);
                nodeRowRects.Add(row);
                bool selected = selectedNode == i;
                ZoundsEditorDiagnostics.Record("row node " + i, row);

                if (dragNode == i) ZUI.FillRect(row, "Accent", new Color(1f, 1f, 1f, 0.06f));
                else if (selected) ZUI.FillRect(row, "Accent", new Color(1f, 1f, 1f, 0.04f));

                // grip
                var grip = new Rect(row.x, row.y, GripW, row.height);
                ZoundsEditorDiagnostics.Record("node.grip", grip);
                EditorGUIUtility.AddCursorRect(grip, MouseCursor.MoveArrow);
                GUI.Label(grip, new GUIContent("≡", "Drag to reorder. Signal flows top to bottom."), EditorStyles.centeredGreyMiniLabel);
                if (evt.type == EventType.MouseDown && evt.button == 0 && grip.Contains(evt.mousePosition)) {
                    dragNode = i; dragTargetIndex = i; evt.Use();
                }

                // enable
                var onRect = new Rect(grip.xMax + 2f, row.y + 1f, OnW, row.height - 2f);
                ZoundsEditorDiagnostics.Record("node.on", onRect);
                bool wasOn = node.enabled;
                bool on = ZUI.Toggle(onRect, wasOn, new GUIContent("On", wasOn ? "Bypass this effect (its state is kept)." : "Enable this effect."), ZUI.Style.RichToggle);
                if (on != wasOn) { int ni = i; Modify(zound, on ? "enable effect" : "bypass effect", () => { chain.nodes[ni].enabled = on; chain.Touch(); }); }

                // name, then either every setting inline on this row, or a summary that expands.
                //
                // An effect whose settings fit on its own row at the window's current width shows them right there, with
                // no expand step — the owner's ask for the few-slider effects, applied by width rather than by a list, so a
                // wide window inlines more and a narrow one expands more, and nothing is ever squeezed. Only an effect that
                // does not fit keeps the summary-and-expand pattern, and its settings then flow across the whole width.
                var nameRect = new Rect(onRect.xMax + 6f, row.y, NameW, row.height);
                ZoundsEditorDiagnostics.Record("node.name", nameRect);
                GUI.Label(nameRect, new GUIContent(desc.displayName, desc.summary), EditorStyles.boldLabel);

                var units = NodeUnits(zound, chain, i, node, desc, linked);
                float inlineRoom = AvailableWidth - HeaderLeadW - RemoveW - Gap;
                bool inline = ZUI.WrapRow.OneRowWidth(Gap, Widths(units)) <= inlineRoom;
                if (inline) {
                    float x = nameRect.xMax + Gap;
                    foreach (var u in units) {
                        DrawUnit(zound, chain, u, new Rect(x, row.y + 1f, u.width, row.height - 2f));
                        x += u.width + Gap;
                    }
                    if (selected) selectedNode = -1;
                }
                else {
                    var summaryRect = new Rect(nameRect.xMax, row.y, row.width - (nameRect.xMax - row.x) - RemoveW - 4f, row.height);
                    ZoundsEditorDiagnostics.Record("node.summary", summaryRect);
                    GUI.Label(summaryRect, new GUIContent((selected ? "▾ " : "▸ ") + Summary(zound, chain, i, node, desc),
                        selected ? "Click to fold the settings away." : "Click to show all " + units.Count + " settings. They do not fit on this row at the window's current width."), EditorStyles.miniLabel);
                    if (evt.type == EventType.MouseDown && evt.button == 0 && (nameRect.Contains(evt.mousePosition) || summaryRect.Contains(evt.mousePosition))) {
                        selectedNode = selected ? -1 : i; evt.Use();
                    }
                }

                // remove
                var xRect = new Rect(row.xMax - RemoveW, row.y + 1f, RemoveW, row.height - 2f);
                ZoundsEditorDiagnostics.Record("node.remove", xRect);
                if (ZUI.Button(xRect, new GUIContent("×", "Removes this effect from the chain."), ZUI.Style.RichButton, ZUI.Tint.Danger)) {
                    int ni = i;
                    Modify(zound, "remove effect", () => { chain.RemoveNode(ni); if (selectedNode >= chain.nodes.Count) selectedNode = -1; });
                    GUIUtility.ExitGUI();
                }

                if (!inline && selectedNode == i) {
                    var flow = new ZUI.WrapRow(AvailableWidth, GripW + 6f, RowH, Gap);
                    foreach (var u in units) DrawUnit(zound, chain, u, Inset(flow.Next(u.width)));
                }
            }

            // drop indicator
            if (dragNode >= 0 && evt.type == EventType.MouseDrag) {
                dragTargetIndex = DropIndexAt(evt.mousePosition.y);
                evt.Use();
            }
            if (dragNode >= 0 && dragTargetIndex >= 0 && evt.type == EventType.Repaint && nodeRowRects.Count > 0) {
                float y = dragTargetIndex < nodeRowRects.Count ? nodeRowRects[dragTargetIndex].y : nodeRowRects[nodeRowRects.Count - 1].yMax;
                var line = new Rect(nodeRowRects[0].x, y - 1f, nodeRowRects[0].width, 2f);
                EditorGUI.DrawRect(line, new Color(0.4f, 0.8f, 1f, 0.9f));
            }

            // add
            GUILayout.BeginHorizontal();
            tmp.text = "Add effect…"; tmp.tooltip = "Appends an effect to the end of the chain.";
            if (ZUI.Button(tmp, ZUI.Style.RichButton, ZUICornerMask.All, GUILayout.Width(90f), GUILayout.Height(RowH))) {
                var items = new List<ZUI.ZUIMenuItem>();
                for (int t = 0; t < ZoundEffectDescriptors.EffectTypeCount; t++) {
                    var type = (ZoundEffectType)t;
                    var d = ZoundEffectDescriptors.Get(type);
                    items.Add(ZUI.MenuItem(d.displayName, () => Modify(zound, "add effect", () => { chain.nodes.Add(new ZoundEffectNode(type)); chain.Touch(); selectedNode = chain.nodes.Count - 1; })));
                }
                ZUI.ContextMenu(items.ToArray());
            }
            GUILayout.FlexibleSpace();
            float tail = ZoundEffectDescriptors.TailBudgetSeconds(chain);
            if (tail > 0f) {
                GUI.Label(GUILayoutUtility.GetRect(110f, RowH), new GUIContent("tail " + tail.ToString("0.00") + " s",
                    "How long this chain keeps ringing after the source stops (delay and reverb decay). Audio End waits at most this long."), EditorStyles.miniLabel);
            }
            GUILayout.EndHorizontal();
        }

        private int DropIndexAt(float y) {
            for (int i = 0; i < nodeRowRects.Count; i++) {
                if (y < nodeRowRects[i].center.y) return i;
            }
            return nodeRowRects.Count;
        }

        private void FinishNodeDrag(Zound zound, ZoundEffectChain chain) {
            int from = dragNode, to = dragTargetIndex;
            dragNode = -1; dragTargetIndex = -1;
            if (from < 0 || to < 0) return;
            if (to > from) to--;
            if (to == from || to >= chain.nodes.Count) return;
            Modify(zound, "reorder effects", () => { chain.MoveNode(from, to); selectedNode = to; });
        }

        internal static string Summary(Zound zound, ZoundEffectChain chain, int nodeIndex, ZoundEffectNode node, EffectDesc desc) {
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            for (int k = 0; k < desc.parameters.Length && shown < 3; k++) {
                var pd = desc.parameters[k];
                if (pd.curve == ParamCurve.Toggle && node.p[k] < 0.5f) continue;
                if (shown > 0) sb.Append("  ");
                sb.Append(pd.name).Append(' ').Append(Format(pd, EffectiveValue(zound, chain, nodeIndex, k, node.p[k])));
                if (IsBound(chain, nodeIndex, k)) sb.Append('~');
                shown++;
            }
            return sb.ToString();
        }

        internal static float EffectiveValue(Zound zound, ZoundEffectChain chain, int nodeIndex, int paramIndex, float presetValue) {
            if (zound.chainPresetId != 0 && ZoundChainLibrary.TryGetOverride(zound, nodeIndex, paramIndex, out float v)) return v;
            return presetValue;
        }

        internal static bool IsBound(ZoundEffectChain chain, int nodeIndex, int paramIndex) {
            foreach (var b in chain.bindings) if (b.nodeIndex == nodeIndex && b.paramIndex == paramIndex) return true;
            return false;
        }

        internal static string Format(ParamDesc pd, float v) {
            if (pd.IsChoice) { int i = Mathf.Clamp(Mathf.RoundToInt(v), 0, pd.options.Length - 1); return pd.options[i]; }
            switch (pd.curve) {
                case ParamCurve.Toggle: return v >= 0.5f ? "on" : "off";
                case ParamCurve.Integer: return Mathf.RoundToInt(v).ToString();
                case ParamCurve.Decibel: return v.ToString("+0.0;-0.0;0.0") + " dB";
                default:
                    string s = Mathf.Abs(v) >= 100f ? v.ToString("0") : Mathf.Abs(v) >= 10f ? v.ToString("0.0") : v.ToString("0.00");
                    return string.IsNullOrEmpty(pd.unit) ? s : s + " " + pd.unit;
            }
        }

        // ───────────────────────────── parameters ─────────────────────────────

        /// <summary>One setting, ready to be drawn as a self-labelled control of a known width.</summary>
        internal sealed class ParamUnit {
            public ParamDesc pd;
            public int nodeIndex, paramIndex;
            public float value;
            public bool overridden, bound;
            public Texture[] icons;
            public string warning, warningTip;
            public float width;
            public System.Action<float> onDrag, onSet;
            // A two-handled range made of two settings (a Random modifier's Min and Max).
            public ParamDesc pdHigh; public float valueHigh; public System.Action<float, float> onRange;
            // The UI Toolkit twin reads the high end fresh on every refresh (the unit itself is a build-time snapshot).
            public System.Func<float> readHigh;
        }

        private static List<float> Widths(List<ParamUnit> units) {
            var w = new List<float>(units.Count);
            foreach (var u in units) w.Add(u.width);
            return w;
        }

        private static Rect Inset(Rect r) => new Rect(r.x, r.y + 1f, r.width, r.height - 2f);

        internal static readonly GUIStyle measureStyle = new GUIStyle(EditorStyles.label) { fontSize = 12 };

        /// <summary>How wide a setting's control is: fixed for a slider, sized to its words for a toggle or a choice.</summary>
        internal static float UnitWidth(ParamUnit u) {
            float w;
            if (u.icons != null) w = u.icons.Length * ZUI.IconChoiceCellWidth;
            else if (u.pdHigh.name != null) w = SliderUnitW + 60f;
            else if (u.pd.IsChoice) {
                w = 0f;
                foreach (var o in u.pd.options) w += Mathf.Max(38f, measureStyle.CalcSize(new GUIContent(o)).x + 14f);
            }
            else if (u.pd.curve == ParamCurve.Toggle) w = Mathf.Max(64f, measureStyle.CalcSize(new GUIContent(u.pd.name)).x + 20f);
            else w = SliderUnitW;
            if (u.bound) w += TagIconW;
            if (u.warning != null) w += WarnIconW;
            return w;
        }

        private List<ParamUnit> NodeUnits(Zound zound, ZoundEffectChain chain, int nodeIndex, ZoundEffectNode node, EffectDesc desc, bool linked) {
            var list = new List<ParamUnit>(desc.parameters.Length);
            for (int k = 0; k < desc.parameters.Length; k++) {
                int pk = k;
                var u = new ParamUnit {
                    pd = desc.parameters[k], nodeIndex = nodeIndex, paramIndex = k,
                    value = EffectiveValue(zound, chain, nodeIndex, k, node.p[k]),
                    overridden = linked && ZoundChainLibrary.TryGetOverride(zound, nodeIndex, k, out _),
                    bound = IsBound(chain, nodeIndex, k),
                    onDrag = v => {
                        if (linked) ModifyContinuous(zound, "override chain parameter", () => ZoundChainLibrary.SetOverride(zound, nodeIndex, pk, v));
                        else ModifyContinuous(zound, "change effect parameter", () => { node.p[pk] = v; chain.Touch(); });
                        ZoundDspPlayback.PushLiveParam(zound, chain, nodeIndex, pk, v);
                    },
                    onSet = v => {
                        if (linked) Modify(zound, "override chain parameter", () => ZoundChainLibrary.SetOverride(zound, nodeIndex, pk, v));
                        else Modify(zound, "change effect parameter", () => { node.p[pk] = v; chain.Touch(); });
                        ZoundDspPlayback.PushLiveParam(zound, chain, nodeIndex, pk, v);
                    },
                };
                u.width = UnitWidth(u);
                list.Add(u);
            }
            return list;
        }

        /// <summary>What a setting does, for its hover text. What it DOES first, then how to reach it.</summary>
        internal static string ParamTip(ParamDesc pd, bool overridden, bool modulatable) {
            string what = string.IsNullOrEmpty(pd.desc)
                ? pd.name + (string.IsNullOrEmpty(pd.unit) ? "" : " (" + pd.unit + ")") + "."
                : pd.name + ": " + pd.desc + (string.IsNullOrEmpty(pd.unit) ? "" : "  Measured in " + pd.unit + ".");
            if (modulatable) what += " Right-click to modulate it" + (overridden ? " or revert the override." : ".");
            if (overridden) what += " Overridden on this sound; the preset's value is not used here.";
            return what;
        }

        /// <summary>
        /// Draws one setting as its own labelled control inside <paramref name="r"/>: a slider with its name and value in the
        /// track, a toggle whose face is its name, a choice strip, or a strip of icons. Right-click opens its modulation menu.
        /// </summary>
        private void DrawUnit(Zound zound, ZoundEffectChain chain, ParamUnit u, Rect r) {
            var evt = Event.current;
            var pd = u.pd;
            var ctrl = r;
            if (u.bound) ctrl.width -= TagIconW;
            if (u.warning != null) ctrl.width -= WarnIconW;
            bool isEffect = u.nodeIndex != int.MinValue;
            string tip = ParamTip(pd, u.overridden, isEffect && pd.automatable);
            ZoundsEditorDiagnostics.Record("param.control " + pd.name, ctrl);

            if (u.pdHigh.name != null) {
                float lo = u.value, hi = u.valueHigh, lo0 = lo, hi0 = hi;
                ZUI.MicroMinMax(ctrl, ref lo, ref hi, pd.min, pd.max, pd.name + "–" + u.pdHigh.name);
                if (!Mathf.Approximately(lo, lo0) || !Mathf.Approximately(hi, hi0)) u.onRange(lo, hi);
                GUI.Label(ctrl, new GUIContent("", pd.name + " and " + u.pdHigh.name + ": the range a value is drawn from, one per play. " + pd.desc));
            }
            else if (u.icons != null) {
                int cur = Mathf.Clamp(Mathf.RoundToInt(u.value), 0, pd.options.Length - 1);
                var tips = new string[pd.options.Length];
                for (int o = 0; o < tips.Length; o++) tips[o] = pd.name + ": " + (pd.OptionTip(o) ?? pd.options[o]);
                int now = ZUI.IconChoice(ctrl, cur, u.icons, tips);
                if (now != cur) u.onSet(now);
            }
            else if (pd.IsChoice) {
                int cur = Mathf.Clamp(Mathf.RoundToInt(u.value), 0, pd.options.Length - 1);
                float total = 0f; var ow = new float[pd.options.Length];
                for (int o = 0; o < ow.Length; o++) { ow[o] = Mathf.Max(38f, measureStyle.CalcSize(new GUIContent(pd.options[o])).x + 14f); total += ow[o]; }
                float x = ctrl.x, scale = ctrl.width / Mathf.Max(1f, total);
                for (int o = 0; o < ow.Length; o++) {
                    var or = new Rect(x, ctrl.y, ow[o] * scale, ctrl.height);
                    x += or.width;
                    var corner = ow.Length == 1 ? ZUICornerMask.All : o == 0 ? ZUICornerMask.Left : o == ow.Length - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                    string otip = pd.name + ": " + (pd.OptionTip(o) ?? pd.options[o]);
                    if (ZUI.Toggle(or, cur == o, new GUIContent(pd.options[o], otip), ZUI.Style.RichToggle, null, corner) && cur != o) u.onSet(o);
                }
            }
            else if (pd.curve == ParamCurve.Toggle) {
                // The toggle's own face is its name, latching when on. No "On/Off" beside a separate label.
                bool on = u.value >= 0.5f;
                bool now = ZUI.Toggle(ctrl, on, new GUIContent(pd.name + (u.overridden ? " •" : ""), tip), ZUI.Style.RichToggle);
                if (now != on) u.onSet(now ? 1f : 0f);
            }
            else {
                // Name and value inside the track, in the parameter's own units.
                string label = pd.name + (u.overridden ? " •" : "") + "  " + Format(pd, u.value);
                if (pd.curve == ParamCurve.Logarithmic) {
                    float lmin = Mathf.Log(Mathf.Max(pd.min, 1e-4f)), lmax = Mathf.Log(Mathf.Max(pd.max, 1e-4f));
                    float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(u.value, 1e-4f)));
                    float nt = ZUI.MicroSlider(ctrl, t, 0f, 1f, label, ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(pd.def, 1e-4f))));
                    if (!Mathf.Approximately(nt, t)) u.onDrag(Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)));
                }
                else {
                    float nv = ZUI.MicroSlider(ctrl, u.value, pd.min, pd.max, label, ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, pd.def);
                    if (pd.curve == ParamCurve.Integer) nv = Mathf.Round(nv);
                    if (!Mathf.Approximately(nv, u.value)) u.onDrag(nv);
                }
                GUI.Label(ctrl, new GUIContent("", tip));
            }

            float right = ctrl.xMax;
            if (u.bound) {
                DrawLiveValueOverlay(zound, ctrl, pd, u.nodeIndex, u.paramIndex, u.value);
                var tag = new Rect(right, r.y, TagIconW, r.height);
                ZoundsEditorDiagnostics.Record("param.tag " + pd.name, tag);
                GUI.Label(tag, new GUIContent("~", "Modulated by " + BoundBy(chain, u.nodeIndex, u.paramIndex) + ". The slider sets where the modifier starts from; the thin line marks it, and the fill shows where the engine has it right now."), EditorStyles.centeredGreyMiniLabel);
                right += TagIconW;
            }
            if (u.warning != null) {
                var ws = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                ws.normal.textColor = new Color(0.97f, 0.8f, 0.4f);
                GUI.Label(new Rect(right, r.y, WarnIconW, r.height), new GUIContent("⚠", u.warning + ". " + u.warningTip), ws);
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && r.Contains(evt.mousePosition)) {
                ShowParamMenu(zound, chain, u.nodeIndex, u.paramIndex, pd, u.overridden);
                evt.Use();
            }
        }

        /// <summary>
        /// Set while at least one modulated parameter had a live value to draw this frame, so the window knows to keep
        /// redrawing — and, just as importantly, knows to STOP when nothing is playing.
        /// </summary>
        private bool liveParamsAnimating;

        /// <summary>
        /// Draws, on top of a modulated parameter's slider, what the engine is actually using for it right now.
        ///
        /// **Why a slider needs this at all.** Attaching an oscillator to a parameter turns the number on the slider into
        /// only half the story: the slider says where the value was placed, and the engine is somewhere else entirely, moving.
        /// Before this the only way to find out where was to listen and infer, which is exactly the kind of guessing the rest
        /// of this editor exists to remove. So the authored value stays visible as a thin bright marker — it is still the
        /// thing being edited, and it must not move while being dragged — and the live value is shown as fill either extending
        /// past the marker or eaten back from it.
        ///
        /// **Extending and eating back are drawn differently on purpose.** When the engine is above the authored value there
        /// is nothing underneath to cover, so a brighter block is added from the marker outwards. When it is below, the
        /// slider has already drawn fill that is now wrong, so that stretch is dimmed back down. Both end up reading the same
        /// way: colour reaches as far as the engine has taken the value, and the marker stays where the value was set.
        ///
        /// Dimming rather than repainting in the track's colour is deliberate. The track's colour lives in a style sheet
        /// asset, so copying it here would mean a hard-coded colour that silently stops matching the moment the theme is
        /// retouched — and a "cleared" stretch in slightly the wrong shade looks like a rendering fault. A translucent dark
        /// pass reads as "not filled" over whatever the track happens to be, and cannot fall out of step with it.
        ///
        /// **The marker is drawn whether or not a sound is playing; only the moving fill needs a voice.** An earlier version
        /// drew nothing at all while silent, on the reasoning that a modifier's output does not exist without a voice to
        /// evaluate it — which is true of the FILL and false of the marker. The marker is the authored value, which exists
        /// at all times because it is the thing being edited. Leaving it out while silent made the whole feature invisible
        /// in the state the editor spends most of its life in: open, with a chain on screen and nothing playing. Somebody
        /// who had been told the feature was finished would look straight at it and see no change whatsoever, which is
        /// exactly what happened. Drawn always, it also earns its keep while silent, by marking at a glance which sliders
        /// have a modifier on them.
        /// </summary>
        private void DrawLiveValueOverlay(Zound zound, Rect ctrl, ParamDesc pd, int nodeIndex, int paramIndex, float authored) {
            // Modifier parameters are drawn by the same row helper but are not engine parameters, and a choice strip or a
            // toggle has no fill to extend. Only a real slider on a real effect parameter can show this.
            if (nodeIndex < 0 || pd.IsChoice || pd.curve == ParamCurve.Toggle) return;
            if (Event.current.type != EventType.Repaint) return;

            float tAuthored = Normalised(pd, authored);
            if (!ZoundDspPlayback.TryReadLiveParam(zound, nodeIndex, paramIndex, out float liveValue)) {
                // Silent: the fill already ends exactly where the value was placed, so the marker sits on its right edge.
                DrawMarker(ctrl, tAuthored);
                return;
            }

            liveParamsAnimating = true;

            float tLive = Normalised(pd, liveValue);
            if (Mathf.Abs(tLive - tAuthored) < 0.001f) { DrawMarker(ctrl, tAuthored); return; }

            // Inset so the overlay sits inside the slider's own border rather than on top of it.
            var inner = new Rect(ctrl.x + 1f, ctrl.y + 1f, ctrl.width - 2f, ctrl.height - 2f);
            float xAuthored = inner.x + inner.width * tAuthored;
            float xLive = inner.x + inner.width * tLive;

            if (tLive > tAuthored)
                EditorGUI.DrawRect(new Rect(xAuthored, inner.y, xLive - xAuthored, inner.height), new Color(0.45f, 0.75f, 1f, 0.5f));
            else
                EditorGUI.DrawRect(new Rect(xLive, inner.y, xAuthored - xLive, inner.height), new Color(0.04f, 0.04f, 0.06f, 0.72f));

            DrawMarker(ctrl, tAuthored);
        }

        /// <summary>
        /// The thin line showing where the value was placed, which stays put however far the engine wanders.
        ///
        /// It is a bright line with a dark one immediately behind it, and that is not decoration: this line has to be
        /// visible both against a filled track and against an empty one, and those are opposite backgrounds. A single
        /// near-white line vanishes into a pale fill; a single dark one vanishes into the empty track. One of each,
        /// side by side, means one of the two is always the one you can see — at the cost of two units instead of one,
        /// which still reads as a thin edge rather than a band.
        /// </summary>
        private static void DrawMarker(Rect ctrl, float t) {
            var inner = new Rect(ctrl.x + 1f, ctrl.y + 1f, ctrl.width - 2f, ctrl.height - 2f);
            float x = Mathf.Clamp(inner.x + inner.width * t, inner.x + 1f, inner.xMax - 1f);
            EditorGUI.DrawRect(new Rect(x - 1f, inner.y, 1f, inner.height), new Color(0.05f, 0.05f, 0.08f, 0.85f));
            EditorGUI.DrawRect(new Rect(x, inner.y, 1f, inner.height), new Color(0.99f, 0.99f, 1f, 0.95f));
        }

        /// <summary>
        /// Where a value sits along its slider, 0 to 1. This has to match how each control maps its travel or the overlay
        /// would disagree with the fill underneath it — which is why the log case is spelled out again here rather than
        /// assumed linear.
        /// </summary>
        internal static float Normalised(ParamDesc pd, float value) {
            if (pd.curve == ParamCurve.Logarithmic) {
                float lmin = Mathf.Log(Mathf.Max(pd.min, 1e-4f)), lmax = Mathf.Log(Mathf.Max(pd.max, 1e-4f));
                return Mathf.Clamp01(Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(value, 1e-4f))));
            }
            return Mathf.Clamp01(Mathf.InverseLerp(pd.min, pd.max, value));
        }

        internal static string BoundBy(ZoundEffectChain chain, int nodeIndex, int paramIndex) {
            foreach (var b in chain.bindings) {
                if (b.nodeIndex == nodeIndex && b.paramIndex == paramIndex && b.modifierIndex >= 0 && b.modifierIndex < chain.modifiers.Count)
                    return ModifierLabel(chain, b.modifierIndex);
            }
            return "";
        }

        internal static string ModifierLabel(ZoundEffectChain chain, int modifierIndex) {
            var m = chain.modifiers[modifierIndex];
            return string.IsNullOrEmpty(m.name) ? ZoundEffectDescriptors.GetModifier(m.type).displayName + " " + (modifierIndex + 1) : m.name;
        }

        /// <summary>
        /// The same modulation menu every parameter has, for a source-stage parameter drawn outside the chain editor — the
        /// live Speed slider in the time-stretch strip (T-0409), so binding a modifier to it works the way it does anywhere.
        /// </summary>
        internal void ShowSourceParamMenu(Zound zound, int paramIndex) {
            var chain = ZoundDspPlayback.ResolveChain(zound, out _);
            if (chain == null || paramIndex < 0 || paramIndex >= SourceStageParam.Count) return;
            ShowParamMenu(zound, chain, -1, paramIndex, ZoundEffectDescriptors.SourceStageParams[paramIndex], false);
        }

        internal static void ShowParamMenu(Zound zound, ZoundEffectChain chain, int nodeIndex, int paramIndex, ParamDesc pd, bool overridden) {
            var items = new List<ZUI.ZUIMenuItem>();
            if (!pd.automatable) items.Add(ZUI.MenuItem("Not modulatable", null, false, false));
            else {
                for (int m = 0; m < chain.modifiers.Count; m++) {
                    int mi = m;
                    bool already = chain.bindings.Exists(b => b.modifierIndex == mi && b.nodeIndex == nodeIndex && b.paramIndex == paramIndex);
                    items.Add(ZUI.MenuItem("Modulate with/" + ModifierLabel(chain, m), () => Modify(zound, "bind modifier", () => AddBinding(chain, mi, nodeIndex, paramIndex, pd)), already, !already));
                }
                for (int t = 0; t < ZoundEffectDescriptors.ModifierTypeCount; t++) {
                    var type = (ZoundModifierType)t;
                    items.Add(ZUI.MenuItem("Modulate with/New " + ZoundEffectDescriptors.GetModifier(type).displayName.ToLower(), () => Modify(zound, "add modifier", () => {
                        chain.modifiers.Add(new ZoundModifier(type));
                        AddBinding(chain, chain.modifiers.Count - 1, nodeIndex, paramIndex, pd);
                    })));
                }
                var bound = chain.bindings.FindAll(b => b.nodeIndex == nodeIndex && b.paramIndex == paramIndex);
                foreach (var b in bound) {
                    var bb = b;
                    items.Add(ZUI.MenuItem("Remove modulation: " + ModifierLabel(chain, b.modifierIndex), () => Modify(zound, "unbind modifier", () => { chain.bindings.Remove(bb); chain.Touch(); })));
                }
            }
            if (overridden) {
                items.Add(ZUI.MenuSeparator());
                items.Add(ZUI.MenuItem("Revert override (use the preset's value)", () => Modify(zound, "revert chain override", () => ZoundChainLibrary.ClearOverride(zound, nodeIndex, paramIndex))));
                items.Add(ZUI.MenuItem("Apply override to the preset", () => Modify(zound, "apply override to preset", () => {
                    if (ZoundChainLibrary.TryGetOverride(zound, nodeIndex, paramIndex, out float v) && nodeIndex >= 0 && nodeIndex < chain.nodes.Count) {
                        chain.nodes[nodeIndex].EnsureParams(); chain.nodes[nodeIndex].p[paramIndex] = v; chain.Touch();
                        ZoundChainLibrary.ClearOverride(zound, nodeIndex, paramIndex);
                    }
                })));
            }
            ZUI.ContextMenu(items.ToArray());
        }

        internal static void AddBinding(ZoundEffectChain chain, int modifierIndex, int nodeIndex, int paramIndex, ParamDesc pd) {
            chain.bindings.Add(new ZoundModifierBinding {
                modifierIndex = modifierIndex, nodeIndex = nodeIndex, paramIndex = paramIndex,
                // Full strength. Since Shift's depth became a share of the room the parameter has (T-0436), one reaches
                // the ends of the range without ever pinning against them, so the modifier is heard at once and in full;
                // turn it down from there. (It was a quarter of the whole control before, because a whole control each
                // way pinned the parameter two thirds of the time — the owner rightly found a quarter an odd default.)
                combine = Dsp.ModulationCombine.Shift, depth = 1f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
            chain.Touch();
        }

        // ───────────────────────────── modifiers ─────────────────────────────

        private void DrawModifiers(Zound zound, ZoundEffectChain chain, bool linked) {
            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            ZUI.Label("Modifiers", ZUI.ZTextStyle.Subheader, GUILayout.Width(80f));
            GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", "Value sources that drive effect parameters and the source stage (pitch, source gain): envelopes over the play, LFOs, a random value per play, or a step list. Bind one from a parameter's right-click menu."));
            GUILayout.FlexibleSpace();
            tmp.text = "Add modifier…"; tmp.tooltip = "Adds a modifier to the stack; bind it to a parameter from that parameter's right-click menu.";
            if (ZUI.Button(tmp, ZUI.Style.RichButton, ZUICornerMask.All, GUILayout.Width(100f), GUILayout.Height(RowH))) {
                var items = new List<ZUI.ZUIMenuItem>();
                for (int t = 0; t < ZoundEffectDescriptors.ModifierTypeCount; t++) {
                    var type = (ZoundModifierType)t;
                    var d = ZoundEffectDescriptors.GetModifier(type);
                    items.Add(ZUI.MenuItem(d.displayName, () => Modify(zound, "add modifier", () => { chain.modifiers.Add(new ZoundModifier(type)); chain.Touch(); })));
                }
                ZUI.ContextMenu(items.ToArray());
            }
            GUILayout.EndHorizontal();

            // Source-stage parameters live here so they can be modulated like any node parameter.
            DrawSourceStageRows(zound, chain);

            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m];
                mod.EnsureParams();
                var desc = ZoundEffectDescriptors.GetModifier(mod.type);
                bool folded = foldedModifiers.Contains(mod);
                var row = GUILayoutUtility.GetRect(1f, RowH, GUILayout.ExpandWidth(true));
                ZUI.FillRect(row, "Accent", new Color(1f, 1f, 1f, 0.04f));
                var evt = Event.current;
                ZoundsEditorDiagnostics.Record("row modifier " + m, row);

                var fold = new Rect(row.x, row.y, GripW, row.height);
                ZoundsEditorDiagnostics.Record("mod.fold", fold);
                GUI.Label(fold, new GUIContent(folded ? "▸" : "▾", folded ? "Expand" : "Collapse"), EditorStyles.centeredGreyMiniLabel);
                if (evt.type == EventType.MouseDown && evt.button == 0 && fold.Contains(evt.mousePosition)) {
                    if (folded) foldedModifiers.Remove(mod); else foldedModifiers.Add(mod);
                    evt.Use();
                }
                var onRect = new Rect(fold.xMax + 2f, row.y + 1f, OnW, row.height - 2f);
                bool on = ZUI.Toggle(onRect, mod.enabled, new GUIContent("On", mod.enabled ? "Disable: its bindings stop applying." : "Enable this modifier."), ZUI.Style.RichToggle);
                if (on != mod.enabled) { Modify(zound, on ? "enable modifier" : "disable modifier", () => { mod.enabled = on; chain.Touch(); }); }

                var typeRect = new Rect(onRect.xMax + 6f, row.y, 62f, row.height);
                GUI.Label(typeRect, new GUIContent(desc.displayName, desc.summary), EditorStyles.boldLabel);
                var nameRect = new Rect(typeRect.xMax + 2f, row.y + 1f, 120f, row.height - 2f);
                ZoundsEditorDiagnostics.Record("mod.on", onRect); ZoundsEditorDiagnostics.Record("mod.type", typeRect); ZoundsEditorDiagnostics.Record("mod.name", nameRect);
                string newName = EditorGUI.TextField(nameRect, mod.name);
                if (newName != mod.name) Modify(zound, "rename modifier", () => mod.name = newName);
                GUI.Label(nameRect, new GUIContent("", "The name shown on the parameters this modifier drives."));

                var targetsRect = new Rect(nameRect.xMax + 6f, row.y, row.width - (nameRect.xMax - row.x) - RemoveW - 10f, row.height);
                ZoundsEditorDiagnostics.Record("mod.targets", targetsRect); ZoundsEditorDiagnostics.Record("mod.remove", new Rect(row.xMax - RemoveW, row.y + 1f, RemoveW, row.height - 2f));
                GUI.Label(targetsRect, new GUIContent(TargetsSummary(chain, m), "The parameters this modifier drives."), EditorStyles.miniLabel);

                var xRect = new Rect(row.xMax - RemoveW, row.y + 1f, RemoveW, row.height - 2f);
                if (ZUI.Button(xRect, new GUIContent("×", "Removes this modifier and every binding that uses it."), ZUI.Style.RichButton, ZUI.Tint.Danger)) {
                    int mi = m;
                    Modify(zound, "remove modifier", () => chain.RemoveModifier(mi));
                    GUIUtility.ExitGUI();
                }

                if (!folded) {
                    DrawModifierBody(zound, chain, m, mod, desc);
                    DrawBindings(zound, chain, m);
                }
            }
        }

        private void DrawSourceStageRows(Zound zound, ZoundEffectChain chain) {
            for (int k = 0; k < SourceStageParam.Count; k++) {
                var pd = ZoundEffectDescriptors.SourceStageParams[k];
                if (!IsBound(chain, -1, k)) continue; // only shown while something drives it; the base values live on the zound itself
                var row = GUILayoutUtility.GetRect(1f, RowH, GUILayout.ExpandWidth(true));
                var labelRect = new Rect(row.x + GripW + 6f, row.y, LabelW, row.height);
                GUI.Label(labelRect, new GUIContent("Source " + pd.name.ToLower(), "Source-stage parameter (applied while reading the sample data, ahead of every effect). Right-click to change its modulation."));
                var tag = new Rect(labelRect.xMax + 4f, row.y, 200f, row.height);
                GUI.Label(tag, new GUIContent("~ " + BoundBy(chain, -1, k), "Modulated by this modifier."), EditorStyles.miniLabel);
                var evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 1 && row.Contains(evt.mousePosition)) { ShowParamMenu(zound, chain, -1, k, pd, false); evt.Use(); }
            }
        }

        internal static string TargetsSummary(ZoundEffectChain chain, int modifierIndex) {
            var sb = new System.Text.StringBuilder();
            foreach (var b in chain.bindings) {
                if (b.modifierIndex != modifierIndex) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(TargetLabel(chain, b));
            }
            return sb.Length == 0 ? "not bound — right-click a parameter to bind it" : "→ " + sb;
        }

        internal static string TargetLabel(ZoundEffectChain chain, ZoundModifierBinding b) {
            if (b.nodeIndex < 0) return "Source " + ZoundEffectDescriptors.SourceStageParams[Mathf.Clamp(b.paramIndex, 0, SourceStageParam.Count - 1)].name.ToLower();
            if (b.nodeIndex >= chain.nodes.Count) return "?";
            var d = ZoundEffectDescriptors.Get(chain.nodes[b.nodeIndex].type);
            string p = b.paramIndex < d.parameters.Length ? d.parameters[b.paramIndex].name : "?";
            return d.displayName + " " + (b.nodeIndex + 1) + " " + p;
        }

        private void DrawModifierBody(Zound zound, ZoundEffectChain chain, int m, ZoundModifier mod, ModifierDesc desc) {
            var units = new List<ParamUnit>(desc.parameters.Length);
            for (int k = 0; k < desc.parameters.Length; k++) {
                var pd = desc.parameters[k];
                int pk = k;
                // LFO: 'New target every' only matters in Random mode; hide it otherwise.
                if (mod.type == ZoundModifierType.Lfo && k == 5 && (int)mod.p[4] != (int)LfoMode.Random) continue;
                // Step: the interval only matters per interval.
                if (mod.type == ZoundModifierType.Step && k == 1 && (int)mod.p[0] != (int)StepTiming.PerInterval) continue;
                // Step: Smooth glides between steps within a play, so it only exists per interval.
                if (mod.type == ZoundModifierType.Step && k == 5 && (int)mod.p[0] != (int)StepTiming.PerInterval) continue;
                // Step: a list running on its own clock (per interval, Retrigger off) has no start to randomise.
                if (mod.type == ZoundModifierType.Step && k == 3 && (int)mod.p[0] == (int)StepTiming.PerInterval && mod.p[4] < 0.5f) continue;
                // Random: Max is drawn together with Min, as one two-handled range.
                if (mod.type == ZoundModifierType.Random && k == 1) continue;

                var u = new ParamUnit {
                    pd = pd, nodeIndex = int.MinValue, paramIndex = k, value = mod.p[k],
                    onDrag = v => ModifyContinuous(zound, "change modifier parameter", () => { mod.p[pk] = v; chain.Touch(); }),
                    onSet = v => Modify(zound, "change modifier parameter", () => { mod.p[pk] = v; chain.Touch(); }),
                };
                // A choice between shapes is shown as the shapes.
                if (mod.type == ZoundModifierType.Lfo && k == 2)
                    u.icons = new Texture[] { ZUIWaveIcons.Get(ZUIWave.Sine), ZUIWaveIcons.Get(ZUIWave.Triangle), ZUIWaveIcons.Get(ZUIWave.Saw), ZUIWaveIcons.Get(ZUIWave.Square) };
                if (mod.type == ZoundModifierType.Lfo && k == 4)
                    u.icons = new Texture[] { ZUIWaveIcons.Get(ZUIWave.Sine), ZUIWaveIcons.Get(ZUIWave.Random) };
                if (mod.type == ZoundModifierType.Random && k == 0) {
                    u.pdHigh = desc.parameters[1];
                    // Shown as the engine uses them: a value saved under the old -4..4 range is held to -1..1 when drawn (T-0447).
                    u.value = Mathf.Clamp(mod.p[0], pd.min, pd.max);
                    u.valueHigh = Mathf.Clamp(mod.p[1], u.pdHigh.min, u.pdHigh.max);
                    u.onRange = (lo, hi) => ModifyContinuous(zound, "change modifier range", () => { mod.p[0] = lo; mod.p[1] = hi; chain.Touch(); });
                }
                if (mod.type == ZoundModifierType.Lfo && k == 1 && (int)mod.p[4] == (int)LfoMode.Oscillate)
                    { EnsureModulationMeasured(zound, chain); SlowRateWarning(modulation.playSeconds, mod, out u.warning, out u.warningTip); }
                u.width = UnitWidth(u);
                units.Add(u);
            }
            var flow = new ZUI.WrapRow(AvailableWidth, GripW + 6f, RowH, Gap);
            foreach (var u in units) DrawUnit(zound, chain, u, Inset(flow.Next(u.width)));

            if (mod.type == ZoundModifierType.Envelope || mod.type == ZoundModifierType.Lfo) {
                if (mod.curve == null) mod.curve = new Envelope(0f, 1f);
                if (!envelopeGuis.TryGetValue(mod, out var gui)) { gui = new EnvelopeGUI { name = "mod" + m }; envelopeGuis.Add(mod, gui); }
                var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
                var color = mod.type == ZoundModifierType.Envelope ? editorStyle.volumeEnvelopeColor : editorStyle.pitchEnvelopeColor;
                // A caption, because without one this graph was being read as the oscillator's WAVEFORM.
                //
                // That misreading is entirely the interface's fault: an unlabelled curve drawn directly beneath a row of
                // buttons called Sine, Triangle, Saw and Square looks like the place you go to draw your own shape. It is
                // not. On an oscillator this curve is a second, slower control that sets HOW STRONGLY the oscillation
                // applies as the sound plays — so the wobble can fade in, fade away, or come and go — while the shape of
                // the wobble itself stays whichever of the four was chosen. It starts flat, so it does nothing until it
                // is drawn on.
                bool isLfoRamp = mod.type == ZoundModifierType.Lfo;
                string caption = isLfoRamp ? "strength over the play" : "shape over the play";
                string curveTip = isLfoRamp
                    ? "How strongly this oscillator applies as the sound plays, from its start on the left to its end on the right. It does NOT change the wave's shape — that is the Shape buttons above. Flat at the top means full strength throughout, which is how it starts. Drag points; double-click to add one."
                    : "The value this envelope produces across the play, from its start on the left to its end on the right (plus any extra time). Drag points; double-click to add one.";
                // The caption sits INSIDE the graph, at its top-left, rather than on a row of its own above it.
                var rect = GUILayoutUtility.GetRect(200f, 56f, GUILayout.ExpandWidth(true));
                rect.xMin += GripW + 6f;
                var evt = Event.current;
                if (evt.type == EventType.Repaint) {
                    EnsureModulationMeasured(zound, chain);
                    DrawCurveBackdrop(rect, m, mod, isLfoRamp, caption, chain);
                }
                GUI.Label(rect, new GUIContent("", curveTip + (isLfoRamp
                    ? " Behind the curve, faintly: what the oscillator actually puts out across the play, measured from the engine — the curve is the ceiling that wobble can reach."
                    : "") + " While the sound plays, an upright line marks the moment being heard."));
                if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && !dragUndoOpen) {
                    dragUndoOpen = true;
                    ZoundsWindow.BeginDragUndo("edit modifier curve");
                    // Editing the Klip's pitch curve moves it off the old scale first, sounding the same (T-0479).
                    KlipChainEnvelopes.EnsurePitchRatioIfPitchCurve(zound, mod);
                }
                AudioSpectrumView.DrawRandomEllipses(rect, mod.curve.GetPointsList(), mod.curve.xMin, mod.curve.xMax, mod.curve.yMin, mod.curve.yMax, color);
                if (gui.Draw(rect, mod.curve, color, 1.5f, true, true)) {
                    chain.Touch();
                    ZoundDspPlayback.InvalidateLayout(zound);
                    EditorUtility.SetDirty(ZoundsProject.Instance);
                }
                if (evt.type == EventType.Repaint) DrawCurvePlayhead(zound, rect, mod, isLfoRamp);
            }
            if (mod.type == ZoundModifierType.Step) DrawSteps(zound, chain, mod);
        }

        /// <summary>
        /// Warns, beside an oscillator's rate, when one cycle takes longer than a play of the sound.
        ///
        /// This is the answer to "I chose a sine and it looks like a ramp". At a tenth of a hertz one cycle takes ten
        /// seconds; a sound that plays for under a second hears a sliver of it — which IS a ramp, and is exactly what the
        /// listener gets. Nothing about the oscillator is wrong, so nothing should silently change; but the reason is
        /// invisible from the settings alone, so it is said here, where the rate is set.
        /// </summary>
        internal static void SlowRateWarning(float play, ZoundModifier mod, out string note, out string noteTip) {
            note = null; noteTip = null;
            float rate = mod.p[1];
            if (play <= 0f || rate <= 0f) return;
            float cycles = rate * play;
            if (cycles >= 1f) return;
            bool freeRunning = mod.p.Length > 3 && mod.p[3] < 0.5f;
            note = Mathf.Max(1, Mathf.RoundToInt(cycles * 100f)) + "% of a cycle per play";
            noteTip = "One cycle at this rate takes " + (1f / rate).ToString("0.0") + " s, but a play of this sound lasts "
                       + play.ToString("0.00") + " s, so each play hears only " + Mathf.RoundToInt(cycles * 100f)
                       + "% of one cycle. That sounds like a slow sweep, not a wobble, whatever the shape."
                       + (freeRunning ? " Because it runs Always, each play also picks it up wherever it has got to, so every play hears a different part of the cycle." : " Because it runs Per play, every play hears the same opening part of the cycle.")
                       + " For at least one full cycle per play, set the rate above " + (1f / play).ToString("0.0#") + " Hz.";
        }

        /// <summary>
        /// A readable ground for a curve over the play: a dark field, quarter lines across and along, and the ends labelled.
        ///
        /// The curve used to be drawn straight onto the window's own background, a thin line with nothing around it — so
        /// there was no telling where "none" and "full" were, or where along the play a point sat. For an oscillator's
        /// strength curve it also draws what the oscillator actually puts out across the play, rectified and faint: the
        /// curve then reads as what it is, the ceiling that wobble is allowed to reach, instead of as a mysterious second
        /// wave. Measured from the engine, not re-derived here, so it cannot disagree with what is heard.
        /// </summary>
        private void DrawCurveBackdrop(Rect rect, int modifierIndex, ZoundModifier mod, bool isLfoRamp, string caption, ZoundEffectChain currentChain) {
            EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.12f));
            var grid = new Color(1f, 1f, 1f, 0.06f);
            for (int q = 1; q < 4; q++) {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height * q / 4f, rect.width, 1f), grid);
                EditorGUI.DrawRect(new Rect(rect.x + rect.width * q / 4f, rect.y, 1f, rect.height), grid);
            }
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.12f));

            var outputs = modulation.modifierOutput;
            if (isLfoRamp && outputs != null && modifierIndex < outputs.Length && outputs[modifierIndex] != null && outputs[modifierIndex].Length > 1) {
                var v = outputs[modifierIndex];
                // The output never reaches beyond Amount × strength, whatever the Offset, so dividing by Amount gives a
                // height that the strength curve is exactly the ceiling of.
                float amount = Mathf.Abs(mod.p[0]);
                if (amount > 1e-6f) {
                    // One column per real screen pixel, so the columns neither overlap nor leave gaps on a dense display.
                    float px = 1f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                    int cols = Mathf.Max(1, Mathf.FloorToInt(rect.width / px));
                    for (int c = 0; c < cols; c++) {
                        float idx = (c + 0.5f) / cols * (v.Length - 1);
                        int i = Mathf.FloorToInt(idx);
                        float s = i >= v.Length - 1 ? v[v.Length - 1] : Mathf.Lerp(v[i], v[i + 1], idx - i);
                        float h = Mathf.Clamp01(Mathf.Abs(s) / amount) * rect.height;
                        EditorGUI.DrawRect(new Rect(rect.x + c * px, rect.yMax - h, px, h), new Color(0.6f, 0.75f, 1f, 0.16f));
                    }
                }
            }

            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft, fontSize = 9 };
            style.normal.textColor = new Color(0.62f, 0.62f, 0.68f);
            var capStyle = new GUIStyle(style) { fontSize = 10 };
            capStyle.normal.textColor = new Color(0.8f, 0.8f, 0.86f);
            GUI.Label(new Rect(rect.x + 9f, rect.y, 200f, 13f), caption, capStyle);
            var topRight = new GUIStyle(style) { alignment = TextAnchor.UpperRight };
            // What the top, middle and bottom mean for what the curve drives (T-0479), with a centre line where the
            // middle is "no change" (a Ratio curve).
            string top = isLfoRamp ? "full" : "top", bottom = isLfoRamp ? "none" : "bottom", mid = null;
            if (!isLfoRamp) KlipChainEnvelopes.CurveAxis(currentChain, mod, out top, out mid, out bottom);
            if (mid != null) {
                float my = Mathf.Round(rect.y + rect.height * 0.5f);
                EditorGUI.DrawRect(new Rect(rect.x, my, rect.width, 1f), new Color(1f, 1f, 1f, 0.18f));
                GUI.Label(new Rect(rect.x + 9f, my - 13f, 60f, 12f), mid, style);
            }
            GUI.Label(new Rect(rect.xMax - 62f, rect.y, 60f, 12f), top, topRight);
            GUI.Label(new Rect(rect.x + 9f, rect.yMax - 13f, 60f, 12f), bottom, style);
            if (modulation.playSeconds > 0f)
                GUI.Label(new Rect(rect.xMax - 62f, rect.yMax - 13f, 60f, 12f), modulation.playSeconds.ToString("0.00") + " s", topRight);
        }

        /// <summary>
        /// The moment of the play being heard, while the sound plays: an upright line across the curve and a dot where it
        /// crosses it. Uses the same time the engine uses for this curve — the play's length for an oscillator's strength,
        /// the play plus the envelope's extra time for an envelope — so the dot is on the value actually being applied.
        /// </summary>
        private static void DrawCurvePlayhead(Zound zound, Rect rect, ZoundModifier mod, bool isLfoRamp) {
            if (!Dsp.SapVoiceRegistry.TryReadPlayPosition(zound, out float elapsed, out float duration) || duration <= 0f) return;
            float extra = !isLfoRamp && mod.p != null && mod.p.Length > 0 ? Mathf.Max(0f, mod.p[0]) : 0f;
            float frac = Mathf.Clamp01(elapsed / (duration + extra));
            float x = rect.x + frac * rect.width;
            EditorGUI.DrawRect(new Rect(x - 0.5f, rect.y, 1.5f, rect.height), new Color(1f, 1f, 1f, 0.9f));
            if (mod.curve != null) {
                float y01 = Mathf.InverseLerp(0f, 1f, mod.curve.Evaluate(frac));
                float y = rect.yMax - y01 * rect.height;
                EditorGUI.DrawRect(new Rect(x - 3f, y - 3f, 6f, 6f), new Color(1f, 0.85f, 0.35f));
            }
        }

        /// <summary>
        /// The most steps a list can hold. The engine itself has no limit, but round-robin order remembers which steps it
        /// has used in a 24-slot mask, so a longer list would quietly stop being shuffled properly.
        /// </summary>
        internal const int MaxSteps = 24;
        internal const float StepBarMaxW = 44f;
        internal const float StepBandH = 64f;

        /// <summary>
        /// The step list, as a row of bars you drag to set — one bar per step, like a step sequencer or an equaliser.
        ///
        /// It used to be a number box per step, which the owner rejected outright: a list of values that play one after
        /// another is a SHAPE, and typing numbers into boxes shows none of it. As bars it reads at a glance (which steps are
        /// high, which low, where the pattern jumps), and a sweep of the mouse across the row sets several at once.
        ///
        /// The bars run from -1 to +1 about a middle line, the same scale an oscillator's output uses at Amount 1, so a step
        /// at the top moves a bound parameter exactly as far as an oscillator's peak would: under Shift, up by the binding's
        /// depth; under Set, to the top of the parameter's range. A value saved outside that range (possible with the old
        /// number boxes) is shown with a bright cap at the edge and kept as it is until that bar is dragged.
        /// </summary>
        private void DrawSteps(Zound zound, ZoundEffectChain chain, ZoundModifier mod) {
            if (mod.steps == null || mod.steps.Length == 0) mod.steps = new float[] { 1f };
            int n = mod.steps.Length;

            // The bars, with the add and remove buttons beside them on the same row. Each bar gets up to a fixed width rather
            // than a share of the whole pane, so a two-step list is two bars, not two slabs; a long list narrows to fit.
            var row = GUILayoutUtility.GetRect(10f, StepBandH, GUILayout.ExpandWidth(true));
            row.xMin += GripW + 6f;
            float buttonsW = 22f * 2f + Gap;
            float barW = Mathf.Min(StepBarMaxW, (row.width - buttonsW) / n);
            var band = new Rect(row.x, row.y + 2f, barW * n, StepBandH - 4f);
            var addRect = new Rect(band.xMax + Gap, band.y + (band.height - (RowH - 2f)) * 0.5f, 22f, RowH - 2f);
            var removeRect = new Rect(addRect.xMax, addRect.y, 22f, RowH - 2f);
            GUI.Label(band, new GUIContent("", "Steps: " + n + (n == 1 ? " value" : " values") + " this modifier steps through, one bar each, played left to right (or shuffled, in round-robin order). Drag a bar up or down to set it, or sweep across several to set them all at once; double-click a bar to put it back on the line."));
            var prev = GUI.enabled;
            GUI.enabled = prev && n < MaxSteps;
            if (ZUI.Button(addRect, new GUIContent("+", n < MaxSteps ? "Adds a step at the end, copying the last one." : "A list holds at most " + MaxSteps + " steps: round-robin order cannot keep track of more."),
                           ZUI.Style.RichButton, ZUICornerMask.Left)) {
                Modify(zound, "add step", () => { var s = new List<float>(mod.steps); s.Add(s[s.Count - 1]); mod.steps = s.ToArray(); chain.Touch(); });
            }
            GUI.enabled = prev && n > 1;
            if (ZUI.Button(removeRect, new GUIContent("−", n > 1 ? "Removes the last step." : "A list needs at least one step."),
                           ZUI.Style.RichButton, ZUICornerMask.Right)) {
                Modify(zound, "remove step", () => { var s = new List<float>(mod.steps); s.RemoveAt(s.Count - 1); mod.steps = s.ToArray(); chain.Touch(); });
            }
            GUI.enabled = prev;
            var steps = mod.steps;
            // What "no change" is depends on how the list is bound. Shifting or setting a parameter: nought. Scaling it:
            // one — times one leaves it alone, times nought silences it. So when every binding of this list scales, the
            // bars run from nought to two about a line at one; drawn about nought instead, the middle of the band would
            // read as "unchanged" while it actually multiplied by zero (found on the owner's own chain, T-0433).
            int mi = chain.modifiers.IndexOf(mod);
            bool anyBinding = false, allScale = true;
            foreach (var b in chain.bindings) {
                if (b.modifierIndex != mi) continue;
                anyBinding = true;
                if (Dsp.ChainModulationCompat.CombineOf(b) != Dsp.ModulationCombine.Scale) allScale = false;
            }
            bool scaling = anyBinding && allScale;
            float lo = scaling ? 0f : -1f, hi = scaling ? 2f : 1f, rest = scaling ? 1f : 0f;
            if (ZUI.BandSliders(band, steps, lo, hi, rest, out var edited, ZUI.SliderStyle.Default, rest,
                                i => "Step " + (i + 1) + ": " + (scaling ? "×" + steps[i].ToString("0.00") : steps[i].ToString("+0.00;-0.00;0.00"))
                                   + (scaling ? "  (this list scales what it is bound to: the line is ×1, unchanged; the bottom is ×0" + (steps[i] < 0f ? "; below zero pins the parameter at its minimum" : "") + ")"
                                              : "  (the line is no change; top and bottom are the furthest this list moves what it is bound to)")
                                   + (steps[i] > hi || steps[i] < lo ? ". Beyond the bars' range; dragging it brings it back inside." : ""))) {
                ModifyContinuous(zound, "change step value", () => { mod.steps = edited; chain.Touch(); });
            }
        }

        // The three ways a modulator can combine, in the order the enum declares them.
        //
        // The old labels were the arithmetic symbols, which described what the code did rather than what the user gets, and
        // two of the three were traps: multiplying by an oscillator drove parameters to their end stops, and both of the
        // others took an amount in the parameter's own units, so the number to type was unguessable and different on every
        // parameter. These say what happens to the sound.
        internal static readonly string[] combineLabels = { "Shift", "Set", "Scale", "Ratio" };
        internal static readonly string[] combineTips = {
            "Shift: moves the parameter away from where you set it. The slider stays your starting point and the modifier pushes it up or down from there — an oscillator swings it both ways around your value, a step list moves it to a different offset on each step. At full depth it can reach all the way to either end of the parameter's range but never past it. Example: a low-pass set to 1 kHz with an oscillator on Shift sweeps up towards 20 kHz and down towards 20 Hz around your 1 kHz.",
            "Set: the modifier takes the parameter over completely and drives it across its whole range, ignoring where the slider is; the modifier's lowest output is the bottom of the range, its highest the top. Depth blends between your slider value (nought) and the modifier's (one). Example: a step list on Set picks the cutoff outright for each step.",
            "Scale: multiplies your value by the modifier's output — one leaves it unchanged, a half halves it, nought silences it. Depth blends from no effect (nought) to the full multiplication (one). Best for levels such as gain or a mix, for a proportional tremolo; offered only where the parameter does not rest at zero, since multiplying zero leaves zero.",
            "Ratio: multiplies your value by a ratio read from the curve, evenly spaced and the same distance up as down: the middle of the curve is no change, the top ×4 and the bottom ×¼ (two octaves either way on a pitch). A flat curve in the middle leaves the sound exactly as it is. Depth shrinks the swing: a half reaches ×2 and ×½. What a Klip's pitch and time curves use."
        };

        /// <summary>The binding mode behind option <paramref name="o"/> of the mode strip (Shift, Set, Scale, Ratio).</summary>
        internal static ModulationCombine OptionCombine(int o) => o < 3 ? (ModulationCombine)o : ModulationCombine.Ratio;

        /// <summary>The strip option showing <paramref name="c"/>.</summary>
        internal static int OptionOf(ModulationCombine c) => c == ModulationCombine.Ratio ? 3 : c == ModulationCombine.ShiftWholeRange ? 0 : (int)c;

        /// <summary>Ratio is offered for a curve (an envelope, whose output runs 0..1) driving a parameter spaced by ratio.</summary>
        internal static bool RatioOfferedFor(ZoundEffectChain chain, ZoundModifierBinding b)
            => b.modifierIndex >= 0 && b.modifierIndex < chain.modifiers.Count && chain.modifiers[b.modifierIndex].type == ZoundModifierType.Envelope
               && DepthRatioOf(chain, b);

        internal const string RatioNotOfferedTip = "Ratio is for a curve (an Envelope) driving a parameter that is spaced by ratio, such as a pitch, a speed or a frequency.";

        /// <summary>
        /// Changes a binding's mode. Moving a Set curve to Ratio converts its points so it sounds exactly as before
        /// (T-0479); any other change keeps the numbers and reads them the new way, as it always has.
        /// </summary>
        internal static void ChooseCombine(ZoundEffectChain chain, ZoundModifierBinding b, ModulationCombine combine) {
            if (combine == ModulationCombine.Ratio && ChainModulationCompat.CanConvertSetToRatio(chain, b)) {
                ChainModulationCompat.ConvertSetToRatio(chain, b, out _);
                return;
            }
            bool toRatio = combine == ModulationCombine.Ratio && ChainModulationCompat.CombineOf(b) != ModulationCombine.Ratio;
            if (toRatio && b.modifierIndex >= 0 && b.modifierIndex < chain.modifiers.Count) {
                var c = chain.modifiers[b.modifierIndex].curve;
                if (c != null) { c.yMin = 0f; c.yMax = 1f; }
            }
            WriteBinding(chain, b, combine, toRatio ? 1f : b.depth);
        }

        internal static string DepthTip(ModulationCombine c, bool legacyShift) =>
            c == ModulationCombine.Shift
                ? (legacyShift
                    ? "How far this modifier may move the parameter. This binding was made before depth changed meaning and still uses the old one: a share of the parameter's WHOLE range each way, so high values pin it against the ends. Change the depth or the mode and it switches to the current meaning, where one reaches the ends but never pins."
                    : "How far this modifier may move the parameter. Nought: not at all. One: all the way to the ends of its range, never past them. A half: half of the room there is in whichever direction it is being pushed.")
                : c == ModulationCombine.Set
                    ? "How much the modifier takes over. Nought: your slider value, unchanged. One: entirely the modifier's value. In between: a blend of the two."
                    : c == ModulationCombine.Ratio
                        ? "How far the curve's swing reaches. One: the top is ×4 and the bottom ×¼. A half: ×2 and ×½. Nought: no change at all."
                        : "How much the multiplication applies. Nought: no effect. One: your value times the modifier's output. In between: part of the way.";

        /// <summary>
        /// The parameter a binding drives, or a harmless stand-in when it points at something that is no longer there.
        ///
        /// A binding can outlive its target — remove an effect and the bindings onto it are left addressing a parameter
        /// that has gone. The row still has to draw, so a missing target answers with a plain nought-to-one range rather
        /// than throwing in the middle of a repaint.
        /// </summary>
        internal static bool TryTargetParam(ZoundEffectChain chain, ZoundModifierBinding b, out ParamDesc pd) {
            pd = default;
            if (b.nodeIndex < 0) {
                if (b.paramIndex < 0 || b.paramIndex >= SourceStageParam.Count) return false;
                pd = ZoundEffectDescriptors.SourceStageParams[b.paramIndex];
                return true;
            }
            if (chain?.nodes == null || b.nodeIndex >= chain.nodes.Count) return false;
            var desc = ZoundEffectDescriptors.Get(chain.nodes[b.nodeIndex].type);
            if (desc == null || b.paramIndex < 0 || b.paramIndex >= desc.parameters.Length) return false;
            pd = desc.parameters[b.paramIndex];
            return true;
        }

        internal static float DepthMinOf(ZoundEffectChain chain, ZoundModifierBinding b) => TryTargetParam(chain, b, out var pd) ? pd.min : 0f;
        internal static float DepthMaxOf(ZoundEffectChain chain, ZoundModifierBinding b) => TryTargetParam(chain, b, out var pd) ? pd.max : 1f;
        internal static bool DepthRatioOf(ZoundEffectChain chain, ZoundModifierBinding b)
            => TryTargetParam(chain, b, out var pd) && ModulationMath.IsRatioSpaced(pd.curve);

        /// <summary>Whether multiplying could do anything at all to this binding's target — see the enum's own note.</summary>
        internal static bool ScaleIsMeaningfulFor(ZoundEffectChain chain, ZoundModifierBinding b)
            => TryTargetParam(chain, b, out var pd) && ModulationMath.ScaleIsMeaningful(pd.def, pd.min);

        /// <summary>
        /// Writes a binding in the current form, whatever form it was in before.
        ///
        /// Touching either control is what converts an old binding for good. Doing it here rather than in a separate
        /// migration pass means the conversion happens exactly when somebody looks at the thing and adjusts it, with the
        /// result visible and audible immediately, instead of silently rewriting a whole project's chains at once.
        /// </summary>
        internal static void WriteBinding(ZoundEffectChain chain, ZoundModifierBinding b, ModulationCombine combine, float depth) {
            b.combine = combine;
            b.depth = Mathf.Clamp01(depth);
            b.schema = ChainModulationCompat.CURRENT_SCHEMA;
            chain.Touch();
        }

        private void DrawBindings(Zound zound, ZoundEffectChain chain, int modifierIndex) {
            for (int i = 0; i < chain.bindings.Count; i++) {
                var b = chain.bindings[i];
                if (b.modifierIndex != modifierIndex) continue;
                var row = GUILayoutUtility.GetRect(1f, RowH, GUILayout.ExpandWidth(true));
                var labelRect = new Rect(row.x + GripW + 6f, row.y, LabelW + 60f, row.height);
                ZoundsEditorDiagnostics.Record("row binding", row); ZoundsEditorDiagnostics.Record("bind.label", labelRect);
                GUI.Label(labelRect, new GUIContent("→ " + TargetLabel(chain, b), "The parameter this binding drives."), EditorStyles.miniLabel);
                float x = labelRect.xMax + 4f;
                // Read through the compatibility layer so a chain saved in the old form shows what it will actually DO,
                // not the stale setting it was saved with. Touching any control here writes it back in the current form.
                var currentCombine = Dsp.ChainModulationCompat.CombineOf(b);
                // A binding saved before Shift became room-relative still behaves the old way; it shows as Shift, and
                // becomes the current Shift the moment its mode or depth is changed.
                bool legacyShift = currentCombine == Dsp.ModulationCombine.ShiftWholeRange;
                if (legacyShift) currentCombine = Dsp.ModulationCombine.Shift;
                bool scaleWorks = ScaleIsMeaningfulFor(chain, b);
                bool ratioWorks = RatioOfferedFor(chain, b);
                for (int o = 0; o < 4; o++) {
                    var r = new Rect(x + o * 42f, row.y + 1f, 42f, row.height - 2f);
                    var corner = o == 0 ? ZUICornerMask.Left : o == 3 ? ZUICornerMask.Right : ZUICornerMask.None;
                    bool isOp = OptionOf(currentCombine) == o;
                    // Scaling is offered only where it can do something. Showing it greyed with the reason beats offering
                    // it everywhere and having it silently do nothing on the parameters that rest at zero.
                    bool offered = o == 2 ? scaleWorks : o == 3 ? ratioWorks : true;
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = prevEnabled && offered;
                    string tip = offered ? combineTips[o]
                               : o == 3 ? RatioNotOfferedTip
                               : "Scale does nothing on this parameter: it rests at zero, or can go negative, and multiplying either leaves it where it is or flips its sign.";
                    if (ZUI.Toggle(r, isOp, new GUIContent(combineLabels[o], tip), ZUI.Style.RichToggle, null, corner) && !isOp) {
                        var combine = OptionCombine(o); var bb = b;
                        Modify(zound, "change how the modulator combines", () => { ChooseCombine(chain, bb, combine); });
                    }
                    GUI.enabled = prevEnabled;
                }
                var depthRect = new Rect(x + 176f, row.y + 1f, 120f, row.height - 2f);
                ZoundsEditorDiagnostics.Record("bind.ops", new Rect(x, row.y + 1f, 168f, row.height - 2f)); ZoundsEditorDiagnostics.Record("bind.depth", depthRect); ZoundsEditorDiagnostics.Record("bind.remove", new Rect(depthRect.xMax + 4f, row.y + 1f, RemoveW, row.height - 2f));
                // Nought to one, because depth is now a share of THIS parameter's own range rather than an amount in its
                // units. That is what makes the same number mean the same thing on a cutoff and on a mix, and it is why the
                // slider no longer runs to four — there is no such thing as four times a parameter's whole range.
                float shownDepth = Dsp.ChainModulationCompat.DepthOf(b, DepthMinOf(chain, b), DepthMaxOf(chain, b), DepthRatioOf(chain, b));
                float nd = ZUI.MicroSlider(depthRect, shownDepth, 0f, 1f, "Depth", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, 0.25f);
                if (!Mathf.Approximately(nd, shownDepth)) { var bb = b; ModifyContinuous(zound, "change depth", () => { WriteBinding(chain, bb, currentCombine, nd); }); }
                GUI.Label(depthRect, new GUIContent("", DepthTip(currentCombine, legacyShift)));
                var xRect = new Rect(depthRect.xMax + 4f, row.y + 1f, RemoveW, row.height - 2f);
                if (ZUI.Button(xRect, new GUIContent("×", "Removes this binding."), ZUI.Style.RichButton, ZUI.Tint.Danger)) {
                    var bb = b;
                    Modify(zound, "remove binding", () => { chain.bindings.Remove(bb); chain.Touch(); });
                    GUIUtility.ExitGUI();
                }
            }
        }
    }

}
