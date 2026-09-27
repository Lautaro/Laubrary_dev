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

        private const float RowH = 20f;
        private const float GripW = 14f;
        private const float OnW = 30f;
        private const float RemoveW = 20f;
        private const float LabelW = 88f;
        private const float SliderW = 150f;
        private const float ChoiceW = 68f;
        private const float TagW = 56f;
        // One numeric parameter: indent + label + control + modulation tag. Two fit side by side when the row allows.
        private const float ParamColW = GripW + 6f + LabelW + 4f + SliderW + 4f + TagW;

        private int selectedNode = -1;
        private int dragNode = -1;
        private int dragTargetIndex = -1;
        private bool dragUndoOpen;
        private readonly Dictionary<ZoundModifier, EnvelopeGUI> envelopeGuis = new Dictionary<ZoundModifier, EnvelopeGUI>();
        private readonly HashSet<ZoundModifier> foldedModifiers = new HashSet<ZoundModifier>();
        private readonly List<Rect> nodeRowRects = new List<Rect>();
        private static readonly GUIContent tmp = new GUIContent();

        public bool isDragging => dragUndoOpen || dragNode >= 0;

        readonly EditorTools.ChainAnalyserPanel analyser = new EditorTools.ChainAnalyserPanel();

        /// <summary>True while the analyser is showing something live, so the hosting window must keep repainting.</summary>
        public bool wantsContinuousRepaint => analyser.wantsContinuousRepaint;

        // ───────────────────────────── entry ─────────────────────────────

        public void Draw(Zound zound) {
            var chain = ZoundDspPlayback.ResolveChain(zound, out var preset);
            bool linked = preset != null;
            var evt = Event.current;
            if (evt.type == EventType.Repaint) { ZoundsEditorDiagnostics.chainEditorRepaints++; ZoundsEditorDiagnostics.BeginChainEditor(new Rect(0, 0, EditorGUIUtility.currentViewWidth, 0)); }

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
                var warn = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };
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

        private void Modify(Zound zound, string undo, System.Action action) {
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

                // name + summary (click selects)
                var nameRect = new Rect(onRect.xMax + 6f, row.y, 90f, row.height);
                var summaryRect = new Rect(nameRect.xMax, row.y, row.width - (nameRect.xMax - row.x) - RemoveW - 4f, row.height);
                ZoundsEditorDiagnostics.Record("node.name", nameRect); ZoundsEditorDiagnostics.Record("node.summary", summaryRect);
                GUI.Label(nameRect, new GUIContent(desc.displayName, desc.summary), EditorStyles.boldLabel);
                GUI.Label(summaryRect, new GUIContent(Summary(zound, chain, i, node, desc), "Click to edit the parameters."), EditorStyles.miniLabel);
                if (evt.type == EventType.MouseDown && evt.button == 0 && (nameRect.Contains(evt.mousePosition) || summaryRect.Contains(evt.mousePosition))) {
                    selectedNode = selected ? -1 : i; evt.Use();
                }

                // remove
                var xRect = new Rect(row.xMax - RemoveW, row.y + 1f, RemoveW, row.height - 2f);
                ZoundsEditorDiagnostics.Record("node.remove", xRect);
                if (ZUI.Button(xRect, new GUIContent("×", "Removes this effect from the chain."), ZUI.Style.RichButton, ZUI.Tint.Danger)) {
                    int ni = i;
                    Modify(zound, "remove effect", () => { chain.RemoveNode(ni); if (selectedNode >= chain.nodes.Count) selectedNode = -1; });
                    GUIUtility.ExitGUI();
                }

                if (selected) DrawNodeParams(zound, chain, i, node, desc, linked);
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

        private string Summary(Zound zound, ZoundEffectChain chain, int nodeIndex, ZoundEffectNode node, EffectDesc desc) {
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

        private static float EffectiveValue(Zound zound, ZoundEffectChain chain, int nodeIndex, int paramIndex, float presetValue) {
            if (zound.chainPresetId != 0 && ZoundChainLibrary.TryGetOverride(zound, nodeIndex, paramIndex, out float v)) return v;
            return presetValue;
        }

        private static bool IsBound(ZoundEffectChain chain, int nodeIndex, int paramIndex) {
            foreach (var b in chain.bindings) if (b.nodeIndex == nodeIndex && b.paramIndex == paramIndex) return true;
            return false;
        }

        private static string Format(ParamDesc pd, float v) {
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

        // Short parameters share a row two by two when the pane is wide enough; a choice strip takes a row.
        private Rect paramRow; private int paramColumn = -1;
        private Rect NextParamSlot(bool wide) {
            float width = EditorGUIUtility.currentViewWidth;
            bool twoColumns = !wide && width >= 2f * ParamColW;
            if (wide || paramColumn != 0 || !twoColumns) {
                paramRow = GUILayoutUtility.GetRect(1f, RowH, GUILayout.ExpandWidth(true));
                paramColumn = wide || !twoColumns ? -1 : 0;
                return paramRow;
            }
            paramColumn = -1;
            return new Rect(paramRow.x + paramRow.width * 0.5f, paramRow.y, paramRow.width * 0.5f, paramRow.height);
        }
        private void EndParamRows() { paramColumn = -1; }

        private void DrawNodeParams(Zound zound, ZoundEffectChain chain, int nodeIndex, ZoundEffectNode node, EffectDesc desc, bool linked) {
            EndParamRows();
            for (int k = 0; k < desc.parameters.Length; k++) {
                var pd = desc.parameters[k];
                int pk = k;
                float current = EffectiveValue(zound, chain, nodeIndex, k, node.p[k]);
                bool overridden = linked && ZoundChainLibrary.TryGetOverride(zound, nodeIndex, k, out _);
                DrawParamRow(zound, chain, nodeIndex, k, pd, current, overridden,
                    v => {
                        if (linked) ModifyContinuous(zound, "override chain parameter", () => ZoundChainLibrary.SetOverride(zound, nodeIndex, pk, v));
                        else ModifyContinuous(zound, "change effect parameter", () => { node.p[pk] = v; chain.Touch(); });
                        ZoundDspPlayback.PushLiveParam(zound, chain, nodeIndex, pk, v);
                    },
                    v => {
                        if (linked) Modify(zound, "override chain parameter", () => ZoundChainLibrary.SetOverride(zound, nodeIndex, pk, v));
                        else Modify(zound, "change effect parameter", () => { node.p[pk] = v; chain.Touch(); });
                        ZoundDspPlayback.PushLiveParam(zound, chain, nodeIndex, pk, v);
                    });
            }
        }

        /// <summary>One parameter: label, the control the descriptor calls for, a modulation tag; right-click for modulation and overrides.</summary>
        private void DrawParamRow(Zound zound, ZoundEffectChain chain, int nodeIndex, int paramIndex, ParamDesc pd, float value, bool overridden,
                                  System.Action<float> onDrag, System.Action<float> onSet) {
            var row = NextParamSlot(pd.IsChoice);
            var evt = Event.current;
            var labelRect = new Rect(row.x + GripW + 6f, row.y, LabelW, row.height);
            ZoundsEditorDiagnostics.Record("row param " + pd.name, row); ZoundsEditorDiagnostics.Record("param.label " + pd.name, labelRect);
            string tip = pd.name + (string.IsNullOrEmpty(pd.unit) ? "" : " (" + pd.unit + ")") + ". Right-click to modulate it" + (overridden ? " or revert the override." : ".");
            GUI.Label(labelRect, new GUIContent(pd.name + (overridden ? " •" : ""), tip + (overridden ? " Overridden on this zound; the preset's value is not used here." : "")));

            var ctrl = new Rect(labelRect.xMax + 4f, row.y + 1f, SliderW, row.height - 2f);
            bool bound = IsBound(chain, nodeIndex, paramIndex);
            if (pd.IsChoice) {
                float w = Mathf.Min(ChoiceW, (row.width - (ctrl.x - row.x) - 30f) / pd.options.Length);
                int cur = Mathf.Clamp(Mathf.RoundToInt(value), 0, pd.options.Length - 1);
                for (int o = 0; o < pd.options.Length; o++) {
                    var r = new Rect(ctrl.x + o * w, ctrl.y, w, ctrl.height);
                    var corner = o == 0 ? ZUICornerMask.Left : o == pd.options.Length - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                    if (ZUI.Toggle(r, cur == o, pd.options[o], ZUI.Style.RichToggle, null, corner) && cur != o) onSet(o);
                }
                ctrl.width = w * pd.options.Length;
            }
            else if (pd.curve == ParamCurve.Toggle) {
                ctrl.width = 60f;
                bool on = value >= 0.5f;
                bool now = ZUI.Toggle(ctrl, on, on ? "On" : "Off", ZUI.Style.RichToggle);
                if (now != on) onSet(now ? 1f : 0f);
            }
            else if (pd.curve == ParamCurve.Logarithmic) {
                // Log-mapped: the slider travels 0..1 in log space and the label carries the real value.
                float lmin = Mathf.Log(Mathf.Max(pd.min, 1e-4f)), lmax = Mathf.Log(Mathf.Max(pd.max, 1e-4f));
                float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(value, 1e-4f)));
                float nt = ZUI.MicroSlider(ctrl, t, 0f, 1f, Format(pd, value), ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(pd.def, 1e-4f))));
                if (!Mathf.Approximately(nt, t)) onDrag(Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)));
            }
            else {
                int decimals = pd.curve == ParamCurve.Integer ? 0 : (pd.max - pd.min) > 20f ? 1 : 2;
                float nv = ZUI.MicroSlider(ctrl, value, pd.min, pd.max, pd.name, ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, pd.def);
                if (pd.curve == ParamCurve.Integer) nv = Mathf.Round(nv);
                if (!Mathf.Approximately(nv, value)) onDrag(nv);
            }

            ZoundsEditorDiagnostics.Record("param.control " + pd.name, ctrl);
            if (bound) {
                var tag = new Rect(ctrl.xMax + 4f, row.y, TagW, row.height);
                ZoundsEditorDiagnostics.Record("param.tag " + pd.name, tag);
                GUI.Label(tag, new GUIContent("~ " + BoundBy(chain, nodeIndex, paramIndex), "Modulated by this modifier; the slider sets the base value the modifier acts on."), EditorStyles.miniLabel);
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && row.Contains(evt.mousePosition)) {
                ShowParamMenu(zound, chain, nodeIndex, paramIndex, pd, overridden);
                evt.Use();
            }
        }

        private static string BoundBy(ZoundEffectChain chain, int nodeIndex, int paramIndex) {
            foreach (var b in chain.bindings) {
                if (b.nodeIndex == nodeIndex && b.paramIndex == paramIndex && b.modifierIndex >= 0 && b.modifierIndex < chain.modifiers.Count)
                    return ModifierLabel(chain, b.modifierIndex);
            }
            return "";
        }

        private static string ModifierLabel(ZoundEffectChain chain, int modifierIndex) {
            var m = chain.modifiers[modifierIndex];
            return string.IsNullOrEmpty(m.name) ? ZoundEffectDescriptors.GetModifier(m.type).displayName + " " + (modifierIndex + 1) : m.name;
        }

        private void ShowParamMenu(Zound zound, ZoundEffectChain chain, int nodeIndex, int paramIndex, ParamDesc pd, bool overridden) {
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

        private static void AddBinding(ZoundEffectChain chain, int modifierIndex, int nodeIndex, int paramIndex, ParamDesc pd) {
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = modifierIndex, nodeIndex = nodeIndex, paramIndex = paramIndex, op = pd.defaultOp, depth = 1f });
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

        private static string TargetsSummary(ZoundEffectChain chain, int modifierIndex) {
            var sb = new System.Text.StringBuilder();
            foreach (var b in chain.bindings) {
                if (b.modifierIndex != modifierIndex) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(TargetLabel(chain, b));
            }
            return sb.Length == 0 ? "not bound — right-click a parameter to bind it" : "→ " + sb;
        }

        private static string TargetLabel(ZoundEffectChain chain, ZoundModifierBinding b) {
            if (b.nodeIndex < 0) return "Source " + ZoundEffectDescriptors.SourceStageParams[Mathf.Clamp(b.paramIndex, 0, SourceStageParam.Count - 1)].name.ToLower();
            if (b.nodeIndex >= chain.nodes.Count) return "?";
            var d = ZoundEffectDescriptors.Get(chain.nodes[b.nodeIndex].type);
            string p = b.paramIndex < d.parameters.Length ? d.parameters[b.paramIndex].name : "?";
            return d.displayName + " " + (b.nodeIndex + 1) + " " + p;
        }

        private void DrawModifierBody(Zound zound, ZoundEffectChain chain, int m, ZoundModifier mod, ModifierDesc desc) {
            EndParamRows();
            for (int k = 0; k < desc.parameters.Length; k++) {
                var pd = desc.parameters[k];
                int pk = k;
                // LFO: 'New target every' only matters in Random mode; hide it otherwise.
                if (mod.type == ZoundModifierType.Lfo && k == 5 && (int)mod.p[4] != (int)LfoMode.Random) continue;
                // Step: the interval only matters per interval.
                if (mod.type == ZoundModifierType.Step && k == 1 && (int)mod.p[0] != (int)StepTiming.PerInterval) continue;
                DrawParamRow(zound, chain, int.MinValue, k, pd, mod.p[k], false,
                    v => ModifyContinuous(zound, "change modifier parameter", () => { mod.p[pk] = v; chain.Touch(); }),
                    v => Modify(zound, "change modifier parameter", () => { mod.p[pk] = v; chain.Touch(); }));
            }
            EndParamRows();
            if (mod.type == ZoundModifierType.Envelope || mod.type == ZoundModifierType.Lfo) {
                if (mod.curve == null) mod.curve = new Envelope(0f, 1f);
                if (!envelopeGuis.TryGetValue(mod, out var gui)) { gui = new EnvelopeGUI { name = "mod" + m }; envelopeGuis.Add(mod, gui); }
                var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
                var color = mod.type == ZoundModifierType.Envelope ? editorStyle.volumeEnvelopeColor : editorStyle.pitchEnvelopeColor;
                var rect = GUILayoutUtility.GetRect(200f, 56f, GUILayout.ExpandWidth(true));
                rect.xMin += GripW + 6f;
                GUI.Label(rect, new GUIContent("", mod.type == ZoundModifierType.Envelope
                    ? "The curve over the play (0 = start, 1 = end plus the extra time). Drag points; double-click to add one."
                    : "Ramp: scales the LFO's output over the play (0 = start, 1 = end). Drag points; double-click to add one."));
                var evt = Event.current;
                if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && !dragUndoOpen) { dragUndoOpen = true; ZoundsWindow.BeginDragUndo("edit modifier curve"); }
                if (gui.Draw(rect, mod.curve, color, 1.5f, true, true)) {
                    chain.Touch();
                    ZoundDspPlayback.InvalidateLayout(zound);
                    EditorUtility.SetDirty(ZoundsProject.Instance);
                }
            }
            if (mod.type == ZoundModifierType.Step) DrawSteps(zound, chain, mod);
        }

        private void DrawSteps(Zound zound, ZoundEffectChain chain, ZoundModifier mod) {
            if (mod.steps == null || mod.steps.Length == 0) mod.steps = new float[] { 1f };
            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            GUILayout.Space(GripW + 6f);
            GUI.Label(GUILayoutUtility.GetRect(LabelW, RowH), new GUIContent("Steps", "The values the modifier steps through, in order (or random order for round robin)."));
            for (int i = 0; i < mod.steps.Length; i++) {
                int si = i;
                float nv = EditorGUI.FloatField(GUILayoutUtility.GetRect(46f, RowH - 2f, GUILayout.Width(46f)), mod.steps[i]);
                if (!Mathf.Approximately(nv, mod.steps[i])) Modify(zound, "change step value", () => { mod.steps[si] = nv; chain.Touch(); });
            }
            if (ZUI.Button(new GUIContent("+", "Adds a step."), ZUI.Style.RichButton, ZUICornerMask.Left, GUILayout.Width(22f), GUILayout.Height(RowH - 2f))) {
                Modify(zound, "add step", () => { var s = new List<float>(mod.steps); s.Add(s[s.Count - 1]); mod.steps = s.ToArray(); chain.Touch(); });
            }
            var prev = GUI.enabled; GUI.enabled = prev && mod.steps.Length > 1;
            if (ZUI.Button(new GUIContent("−", "Removes the last step."), ZUI.Style.RichButton, ZUICornerMask.Right, GUILayout.Width(22f), GUILayout.Height(RowH - 2f))) {
                Modify(zound, "remove step", () => { var s = new List<float>(mod.steps); s.RemoveAt(s.Count - 1); mod.steps = s.ToArray(); chain.Touch(); });
            }
            GUI.enabled = prev;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static readonly string[] opLabels = { "×", "+", "=" };
        private static readonly string[] opTips = {
            "Multiply: the parameter's value is multiplied by the modifier's output × depth.",
            "Add: the modifier's output × depth is added to the parameter's value.",
            "Replace: the parameter takes the modifier's output × depth."
        };

        private void DrawBindings(Zound zound, ZoundEffectChain chain, int modifierIndex) {
            for (int i = 0; i < chain.bindings.Count; i++) {
                var b = chain.bindings[i];
                if (b.modifierIndex != modifierIndex) continue;
                var row = GUILayoutUtility.GetRect(1f, RowH, GUILayout.ExpandWidth(true));
                var labelRect = new Rect(row.x + GripW + 6f, row.y, LabelW + 60f, row.height);
                ZoundsEditorDiagnostics.Record("row binding", row); ZoundsEditorDiagnostics.Record("bind.label", labelRect);
                GUI.Label(labelRect, new GUIContent("→ " + TargetLabel(chain, b), "The parameter this binding drives."), EditorStyles.miniLabel);
                float x = labelRect.xMax + 4f;
                for (int o = 0; o < 3; o++) {
                    var r = new Rect(x + o * 24f, row.y + 1f, 24f, row.height - 2f);
                    var corner = o == 0 ? ZUICornerMask.Left : o == 2 ? ZUICornerMask.Right : ZUICornerMask.None;
                    bool isOp = (int)b.op == o;
                    if (ZUI.Toggle(r, isOp, new GUIContent(opLabels[o], opTips[o]), ZUI.Style.RichToggle, null, corner) && !isOp) {
                        var op = (ModifierOp)o; var bb = b;
                        Modify(zound, "change binding operator", () => { bb.op = op; chain.Touch(); });
                    }
                }
                var depthRect = new Rect(x + 76f, row.y + 1f, 120f, row.height - 2f);
                ZoundsEditorDiagnostics.Record("bind.ops", new Rect(x, row.y + 1f, 72f, row.height - 2f)); ZoundsEditorDiagnostics.Record("bind.depth", depthRect); ZoundsEditorDiagnostics.Record("bind.remove", new Rect(depthRect.xMax + 4f, row.y + 1f, RemoveW, row.height - 2f));
                float nd = ZUI.MicroSlider(depthRect, b.depth, -4f, 4f, "Depth", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, 1f);
                if (!Mathf.Approximately(nd, b.depth)) { var bb = b; ModifyContinuous(zound, "change binding depth", () => { bb.depth = nd; chain.Touch(); }); }
                GUI.Label(depthRect, new GUIContent("", "Scales the modifier's output before the operator applies it."));
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
