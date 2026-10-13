using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // The older routings, kept so instruments saved with them still show and play as before.
        // 0 (Stack) and 2 (Merge) are the same as setups 4 and 7 and are shown as those setups;
        // the other four have several carriers and keep their own diagram.
        static readonly int[][] FmEdges = { new[] { 0,1, 1,2, 2,3 }, new[] { 0,1, 2,3 }, new[] { 0,2, 1,2, 2,3 }, new[] { 0,1 }, new[] { 0,1, 1,2 }, new int[0] };
        static readonly int[][] FmCarriers = { new[] { 3 }, new[] { 1,3 }, new[] { 3 }, new[] { 1,2,3 }, new[] { 2,3 }, new[] { 0,1,2,3 } };
        static readonly string[] ClassicFmNames = { "Stack", "Pairs", "Merge", "Three carriers", "Split stack", "Parallel" };
        static int EffectiveFmAlgorithm(int algorithm) => algorithm >= 0 && algorithm < 6 ? algorithm : 5;

        // Older subtractive instruments have no FM array. Call this inside the Engine
        // choice's InstrumentEdit, or from the explicit repair action below, so the
        // initialization and its Undo snapshot are one user action.
        static void EnsureFmOperators(InstrumentParameters parameters)
        {
            var existing = parameters.fmOperators;
            if (existing != null && existing.Length >= 4) return;
            var operators = NewInstrumentData().parameters.fmOperators;
            if (existing != null) Array.Copy(existing, operators, existing.Length);
            parameters.fmOperators = operators;
        }

        static string SetupTip(int setup)
        {
            int used = Enumerable.Range(0, 4).Count(s => TrackerFmSetups.Uses(setup, s));
            return "Setup " + setup + ": " + TrackerFmSetups.Formula(setup) + ". " + used + " operators; C is the carrier you hear, the others bend its phase.";
        }

        void BuildFm(VisualElement root, InstrumentParameters q)
        {
            int setup = TrackerFmSetups.SetupOf(q.fmAlgorithm);
            bool classic = setup == 0;
            int algorithm = EffectiveFmAlgorithm(q.fmAlgorithm);
            FmSetupView graph = null;
            var picker = new VisualElement { name = "fm-setups" };
            picker.style.flexDirection = FlexDirection.Row; picker.style.flexWrap = Wrap.Wrap; picker.style.flexShrink = 1; picker.style.minWidth = 0;
            for (int n = 1; n <= TrackerFmSetups.Count; n++)
            {
                int pick = n;
                var tile = new FmSetupTile(pick, pick == setup, () => InstrumentEdit("FM setup " + pick, () => q.fmAlgorithm = TrackerFmSetups.Base + pick, true));
                picker.Add(tile);
            }
            root.Add(Z.Field("Setup", "Pick how the four operators connect. Each tile shows its routing; C is the carrier you hear and A, B and D bend its phase.", picker));
            var extras = Flow();
            // T-0645: how a note starts and how finely FM is computed. Both read for the current state.
            extras.Add(InstrumentToggle("Oversample", !q.fmSingleRate, !q.fmSingleRate
                ? "On: FM is computed at twice the output rate and filtered, so bright, high notes keep clean overtones instead of harsh, inharmonic ones folded back from above the highest audible frequency (aliasing). Costs about a quarter more processing per FM note. Click to compute at the output rate, as FM played before."
                : "Off: FM is computed at the output rate. Cheaper, but on bright, high notes the overtones above the highest audible frequency fold back down as harsh, inharmonic tones (aliasing). Click to compute at twice the rate.",
                v => q.fmSingleRate = !v, "fm-oversample", true));
            extras.Add(InstrumentToggle("Free phase", q.fmFreePhase, q.fmFreePhase
                ? "On: each note starts its operators part-way through their waves, at a point of its own (always the same for the same note in the same place), so repeated notes have slightly different attacks. Click to start every note at the start of its waves, so every attack is identical."
                : "Off: every note starts its operators at the start of their waves, so every attack is identical. Click to start each note part-way through its waves, at a point of its own, for slightly varied attacks.",
                v => q.fmFreePhase = v, "fm-free-phase", true));
            if (classic) extras.Add(Diagnostic("Older routing: " + ClassicFmNames[algorithm], "This instrument uses the older " + ClassicFmNames[algorithm] + " routing" + (q.fmAlgorithm >= 6 ? " (stored as " + q.fmAlgorithm + ", played as Parallel)" : "") + ", which sends more than one operator to the output. It still plays; picking a setup above switches to that setup."));
            // Output feedback is the older whole-instrument feedback into A. New sounds use each
            // modulator's own Feedback instead; the control stays wherever it is in use.
            if (classic || q.fmFeedback != 0) extras.Add(ParameterValue("fmFeedback", q.fmFeedback, v => { q.fmFeedback = v; graph?.MarkDirtyRepaint(); }, "synth-fmFeedback", q, "fmFeedback"));
            root.Add(extras);
            if (q.fmOperators == null || q.fmOperators.Length < 4)
            {
                root.Add(Flow(Button("Create operators", "Add the missing FM operators using the declared synth defaults. Existing operators and their settings stay unchanged; Undo restores the earlier array.", () => InstrumentEdit("create FM operators", () => EnsureFmOperators(q), true), "fm-create-operators"),
                    Diagnostic("Operators missing", "This older instrument contains " + (q.fmOperators?.Length ?? 0) + " of the four FM operators. Create the missing operators to edit or play FM.")));
                return;
            }
            if (q.fmOperators.Length > 4) root.Add(Diagnostic("Extra operators retained", "The first four operators are editable and play. The extra imported operator data remains unchanged in the asset."));
            if (classic) root.Add(new FmRoutingView(algorithm));
            else root.Add(graph = new FmSetupView(setup, false, slot => slot < TrackerFmSetups.Modulators ? q.fmOperators[slot].feedback : 0, () => q.fmFeedback != 0, slot => FmOperatorOff(q, slot)));
            var operators = InstrumentColumns(root, "fm-operators", 380);
            for (int i = 0; i < 4; i++)
            {
                if (!classic && !TrackerFmSetups.Uses(setup, i)) continue;
                int at = i; var op = q.fmOperators[i];
                bool carrier = classic ? FmCarriers[algorithm].Contains(i) : i == TrackerFmSetups.Carrier;
                string title = classic ? "Operator " + (i + 1) + (carrier ? " - Carrier" : " - Modulator") : TrackerFmSetups.Letter(i) + (carrier ? " - Carrier" : " - Modulator");
                string tip = carrier ? "This sine operator reaches the audible output. Its level and envelope control loudness." : classic ? "This sine operator changes the phase of the operator its arrow points to. Its level and envelope control the timbre." : "This sine operator bends the phase of " + string.Join(" and ", Targets(setup, i)) + ". Its level and envelope control the timbre; Feedback makes it bend its own phase.";
                var box = InstrumentSection(operators, title, "fm." + i, tip);
                if (op.waveform != 0) box.Add(Diagnostic("Sine playback", "The imported waveform " + op.waveform + " is preserved; the runtime renders sine for FM operators."));
                var row = Flow(); VisualElement ratioControl = null;
                bool fixedEnvelope = q.GetParameterEnvelope("fmOperators." + at + ".freqFixed")?.enabled == true;
                var fields = new List<string> { "freqRatio", "freqFixed", "level" };
                if (!classic && !carrier) fields.Add("feedback");
                // A fixed-frequency curve can cross zero, in which case the ratio still matters.
                foreach (string field in fields)
                {
                    var f = typeof(ZTrackerInstrument.FMOperatorData).GetField(field); string id = "fmOperators." + at + "." + field;
                    var control = ParameterValue(field, (float)f.GetValue(op), v => { object next = q.fmOperators[at]; f.SetValue(next, v); q.fmOperators[at] = (ZTrackerInstrument.FMOperatorData)next; if (field == "feedback" || field == "level") graph?.Refresh(); if (field == "freqFixed" && ratioControl != null) ratioControl.style.display = v > 0 && q.GetParameterEnvelope("fmOperators." + at + ".freqFixed")?.enabled != true ? DisplayStyle.None : DisplayStyle.Flex; }, id, q, id, field == "level" ? LevelPresentation(classic, carrier, classic ? FmCarriers[algorithm].Length : 1, classic ? null : Targets(setup, i)) : (MusicalRange?)null);
                    if (field == "freqRatio") { ratioControl = control; control.style.display = op.freqFixed > 0 && !fixedEnvelope ? DisplayStyle.None : DisplayStyle.Flex; }
                    if (field == "freqFixed") control.Q<ZuiValueControl>().ModeChanged += () => control.schedule.Execute(() => { ratioControl.style.display = q.fmOperators[at].freqFixed > 0 && q.GetParameterEnvelope("fmOperators." + at + ".freqFixed")?.enabled != true ? DisplayStyle.None : DisplayStyle.Flex; });
                    row.Add(control);
                }
                box.Add(row);
                float Read(string field) => (float)typeof(ZTrackerInstrument.FMOperatorData).GetField(field).GetValue(q.fmOperators[at]);
                void Write(string field, float value) => InstrumentEdit("operator " + field, () => { object next = q.fmOperators[at]; typeof(ZTrackerInstrument.FMOperatorData).GetField(field).SetValue(next, value); q.fmOperators[at] = (ZTrackerInstrument.FMOperatorData)next; });
                box.Add(Named(Z.Adsr("ADSR", () => Read("attack"), v => Write("attack",v), () => Read("decay"), v => Write("decay",v), () => Read("sustain"), v => Write("sustain",v), () => Read("release"), v => Write("release",v), "This operator's attack, decay, sustain and release; each note restarts it. Drag a point or a slider; double-click resets a stage.", EnvelopeDefaults(NewInstrumentData().parameters.fmOperators[at])), "fm-adsr-" + at));
            }
        }

        // An operator's Level means two different things (T-0636): on a carrier it is loudness, on a modulator it is how
        // strongly it bends the phase of the operator it feeds. A modulator at 0 therefore leaves a plainer tone, not
        // silence (measured: in A → B → D → C, D at 0 leaves C as a pure sine; A or B at 0 still leave D bending C).
        static MusicalRange LevelPresentation(bool classic, bool carrier, int carriers, IEnumerable<string> targets)
        {
            var r = Musical("level");
            string bends = targets == null ? "the operator its arrow points to" : string.Join(" and ", targets);
            r.tip = carrier
                ? carriers > 1 ? "How loud this carrier is in the mix: 100% is unity gain. 0% silences only this carrier's branch; the other carriers keep sounding." : "How loud C plays: 100% is unity gain. C is the only operator you hear, so 0% silences the instrument."
                : "How strongly this modulator bends the phase of " + bends + ": its modulation depth, not a volume. 0% switches its effect off, so the sound keeps playing as a plainer tone (a pure sine when nothing else bends C); higher values add brighter overtones.";
            return r;
        }

        // An operator whose Level is 0 and not driven by an envelope does nothing; the diagram fades it.
        static bool FmOperatorOff(InstrumentParameters q, int slot) => q.fmOperators != null && slot < q.fmOperators.Length && q.fmOperators[slot].level == 0 && q.GetParameterEnvelope("fmOperators." + slot + ".level")?.enabled != true;

        static IEnumerable<string> Targets(int setup, int slot)
        {
            var edges = TrackerFmSetups.Edges(setup);
            for (int e = 0; e < edges.Length; e += 2) if (edges[e] == slot) yield return TrackerFmSetups.Letter(edges[e + 1]).ToString();
        }

        // Node placement for a setup: columns by longest path to the carrier, so signal flows
        // left to right; operators that share a column stack, centred.
        struct FmNodeLayout { public int column, row, rows, columns; }
        static FmNodeLayout[] LayoutSetup(int setup)
        {
            var edges = TrackerFmSetups.Edges(setup); var depth = new int[4];
            for (int pass = 0; pass < 4; pass++) for (int e = 0; e < edges.Length; e += 2) depth[edges[e]] = Math.Max(depth[edges[e]], depth[edges[e + 1]] + 1);
            int deepest = Enumerable.Range(0, 4).Where(s => TrackerFmSetups.Uses(setup, s)).Max(s => depth[s]);
            var layout = new FmNodeLayout[4]; var perColumn = new int[deepest + 1];
            foreach (int slot in new[] { 0, 1, 2, 3 }) if (TrackerFmSetups.Uses(setup, slot)) { int c = deepest - depth[slot]; layout[slot].column = c; layout[slot].row = perColumn[c]++; }
            for (int s = 0; s < 4; s++) { layout[s].rows = perColumn[layout[s].column]; layout[s].columns = deepest + 1; }
            return layout;
        }

        static readonly Color FmModulationColor = new Color(.45f,.7f,.95f);
        static readonly Color FmCarrierColor = new Color(.5f,.85f,.55f);
        static readonly Color FmFeedbackColor = new Color(.8f,.6f,.9f);

        // One setup's routing: A, B, D and C as boxes, arrows for modulation, and a loop on
        // every modulator whose Feedback is above zero. Compact form is the picker thumbnail.
        sealed class FmSetupView : VisualElement
        {
            readonly int setup; readonly bool compact; readonly Func<int,float> feedback; readonly Func<bool> outputFeedback; readonly Func<int,bool> off;
            readonly FmNodeLayout[] layout; readonly Label[] nodes = new Label[4];
            readonly float nodeWidth, nodeHeight, gapY, top, pad, lane;
            public const float FullHeight = 128, FullMaxWidth = 560;
            public FmSetupView(int setup, bool compact, Func<int,float> feedback = null, Func<bool> outputFeedback = null, Func<int,bool> off = null)
            {
                this.setup = setup; this.compact = compact; this.feedback = feedback ?? (_ => 0); this.outputFeedback = outputFeedback ?? (() => false); this.off = off ?? (_ => false);
                layout = LayoutSetup(setup); name = compact ? "fm-setup-diagram-" + setup : "fm-routing";
                nodeWidth = compact ? 13 : 38; nodeHeight = compact ? 11 : 24; gapY = compact ? 3 : 15; top = compact ? 0 : 14; pad = compact ? 2 : 6; lane = compact ? 5 : 18;
                pickingMode = compact ? PickingMode.Ignore : PickingMode.Position;
                if (!compact) { tooltip = SetupTip(setup) + " Arrows carry phase modulation toward their target. A loop on a modulator is its own Feedback. A faded operator is at Level 0: a faded modulator bends nothing, a faded C is silent."; style.height = FullHeight; style.minHeight = FullHeight; style.flexShrink = 0; style.minWidth = 0; style.width = Length.Percent(100); style.maxWidth = FullMaxWidth; }
                for (int slot = 0; slot < 4; slot++)
                {
                    if (!TrackerFmSetups.Uses(setup, slot)) continue;
                    bool carrier = slot == TrackerFmSetups.Carrier;
                    var label = new Label(TrackerFmSetups.Letter(slot).ToString()) { name = (compact ? "fm-setup-node-" : "fm-routing-node-") + slot };
                    label.pickingMode = compact ? PickingMode.Ignore : PickingMode.Position;
                    label.style.position = Position.Absolute; label.style.width = nodeWidth; label.style.height = nodeHeight;
                    label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
                    label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
                    label.style.fontSize = compact ? 8 : 12; label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.style.backgroundColor = carrier ? new Color(.15f,.24f,.16f) : new Color(.13f,.19f,.27f);
                    label.style.borderBottomColor = label.style.borderLeftColor = label.style.borderRightColor = label.style.borderTopColor = carrier ? FmCarrierColor : FmModulationColor;
                    label.style.borderBottomWidth = label.style.borderLeftWidth = label.style.borderRightWidth = label.style.borderTopWidth = 1;
                    label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = compact ? 2 : 4;
                    nodes[slot] = label; Add(label);
                }
                RegisterCallback<GeometryChangedEvent>(_ => Place());
                generateVisualContent += Paint;
            }
            int MaxRows => layout.Where((l, s) => TrackerFmSetups.Uses(setup, s)).Max(l => l.rows);
            bool HasSkip { get { var e = TrackerFmSetups.Edges(setup); for (int i = 0; i < e.Length; i += 2) if (layout[e[i+1]].column - layout[e[i]].column > 1) return true; return false; } }
            Rect NodeRect(int slot, float width, float height)
            {
                var l = layout[slot]; int maxRows = MaxRows;
                float usable = height - top - pad * 2 - (HasSkip ? lane : 0);
                float block = maxRows * nodeHeight + (maxRows - 1) * gapY;
                float y0 = top + pad + Math.Max(0, (usable - block) / 2) + (maxRows - l.rows) * (nodeHeight + gapY) / 2;
                float span = Math.Max(0, width - pad * 2 - nodeWidth - (compact ? 0 : 20));
                float x = pad + (compact ? 0 : 10) + (l.columns > 1 ? span * l.column / (l.columns - 1) : span / 2);
                return new Rect(x, y0 + l.row * (nodeHeight + gapY), nodeWidth, nodeHeight);
            }
            void Place()
            {
                float w = contentRect.width, h = contentRect.height; if (w <= 0 || h <= 0) return;
                for (int slot = 0; slot < 4; slot++)
                {
                    if (nodes[slot] == null) continue;
                    var r = NodeRect(slot, w, h); nodes[slot].style.left = r.x; nodes[slot].style.top = r.y;
                }
                Refresh();
            }
            /// Re-reads the operators' levels and feedback: fades an operator at Level 0, updates tooltips, repaints.
            public void Refresh()
            {
                for (int slot = 0; slot < 4; slot++)
                {
                    if (nodes[slot] == null) continue;
                    nodes[slot].style.opacity = off(slot) ? .35f : 1f;
                    if (!compact) nodes[slot].tooltip = NodeTip(slot);
                }
                MarkDirtyRepaint();
            }
            string NodeTip(int slot)
            {
                if (slot == TrackerFmSetups.Carrier) return "C is the carrier: the sine you hear. " + (TrackerFmSetups.Edges(setup).Length > 0 ? "Its phase is bent by the operators whose arrows reach it." : "") + (off(slot) ? " Its Level is 0, so the instrument is silent." : "");
                float amount = feedback(slot);
                return TrackerFmSetups.Letter(slot) + " is a modulator: it bends the phase of " + string.Join(" and ", Targets(setup, slot)) + "." + (off(slot) ? " Its Level is 0, so it bends nothing: the sound plays as if it were not there." : "") + (amount > 0 ? " Its Feedback (" + Mathf.RoundToInt(amount * 100) + "%) bends its own phase too, shown as the loop above it." : "");
            }
            void Paint(MeshGenerationContext context)
            {
                float width = contentRect.width, height = contentRect.height; if (width <= 0 || height <= 0) return;
                var painter = context.painter2D; painter.lineWidth = compact ? 1 : 2; painter.strokeColor = FmModulationColor;
                float head = compact ? 2.5f : 5;
                var edges = TrackerFmSetups.Edges(setup);
                float laneY = height - pad - lane / 2;
                for (int e = 0; e < edges.Length; e += 2)
                {
                    var a = NodeRect(edges[e], width, height); var b = NodeRect(edges[e + 1], width, height);
                    // An arrow from an operator at Level 0 carries nothing: drawn faint.
                    var arrow = FmModulationColor; if (off(edges[e])) arrow.a = .3f; painter.strokeColor = arrow;
                    painter.BeginPath();
                    if (layout[edges[e+1]].column - layout[edges[e]].column > 1)
                    {
                        // A route that skips a column runs under the row, into the target from below.
                        painter.MoveTo(new Vector2(a.center.x, a.yMax)); painter.LineTo(new Vector2(a.center.x, laneY)); painter.LineTo(new Vector2(b.center.x, laneY)); painter.LineTo(new Vector2(b.center.x, b.yMax)); painter.Stroke();
                        painter.BeginPath(); painter.MoveTo(new Vector2(b.center.x - head, b.yMax + head * 1.6f)); painter.LineTo(new Vector2(b.center.x, b.yMax)); painter.LineTo(new Vector2(b.center.x + head, b.yMax + head * 1.6f)); painter.Stroke();
                    }
                    else
                    {
                        float mid = (a.xMax + b.xMin) / 2;
                        painter.MoveTo(new Vector2(a.xMax, a.center.y)); painter.LineTo(new Vector2(mid, a.center.y)); painter.LineTo(new Vector2(mid, b.center.y)); painter.LineTo(new Vector2(b.xMin, b.center.y)); painter.Stroke();
                        painter.BeginPath(); painter.MoveTo(new Vector2(b.xMin - head * 1.6f, b.center.y - head)); painter.LineTo(new Vector2(b.xMin, b.center.y)); painter.LineTo(new Vector2(b.xMin - head * 1.6f, b.center.y + head)); painter.Stroke();
                    }
                }
                if (compact) return;
                painter.strokeColor = FmFeedbackColor;
                for (int slot = 0; slot < TrackerFmSetups.Modulators; slot++)
                {
                    if (!TrackerFmSetups.Uses(setup, slot) || feedback(slot) <= 0) continue;
                    var r = NodeRect(slot, width, height); float x0 = r.x + r.width * .25f, x1 = r.x + r.width * .75f, y = r.y - 11;
                    painter.BeginPath(); painter.MoveTo(new Vector2(x1, r.y)); painter.LineTo(new Vector2(x1, y)); painter.LineTo(new Vector2(x0, y)); painter.LineTo(new Vector2(x0, r.y)); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(new Vector2(x0 - 4, r.y - 6)); painter.LineTo(new Vector2(x0, r.y)); painter.LineTo(new Vector2(x0 + 4, r.y - 6)); painter.Stroke();
                }
                if (outputFeedback())
                {
                    // The older whole-instrument feedback: the carrier's output returns into A.
                    var c = NodeRect(TrackerFmSetups.Carrier, width, height); var a = NodeRect(0, width, height); float y = 3, x = Math.Max(2, a.x - 7);
                    painter.BeginPath(); painter.MoveTo(new Vector2(c.xMax, c.center.y)); painter.LineTo(new Vector2(c.xMax + 7, c.center.y)); painter.LineTo(new Vector2(c.xMax + 7, y)); painter.LineTo(new Vector2(x, y)); painter.LineTo(new Vector2(x, a.center.y)); painter.LineTo(new Vector2(a.x, a.center.y)); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(new Vector2(a.x - 6, a.center.y - 4)); painter.LineTo(new Vector2(a.x, a.center.y)); painter.LineTo(new Vector2(a.x - 6, a.center.y + 4)); painter.Stroke();
                }
            }
        }

        // A picker thumbnail: the setup's number over its compact diagram. Clicking picks it.
        sealed class FmSetupTile : VisualElement
        {
            public const float Width = 70, Height = 58;
            public FmSetupTile(int setup, bool selected, Action pick)
            {
                name = "fm-setup-" + setup; tooltip = SetupTip(setup) + (selected ? " (current)" : " Click to use it.");
                style.width = Width; style.height = Height; style.flexShrink = 0;
                style.marginRight = 4; style.marginBottom = 4; style.paddingLeft = style.paddingRight = 3; style.paddingTop = 1; style.paddingBottom = 2;
                style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 4;
                style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = style.borderTopWidth = 1;
                var rest = selected ? new Color(.24f,.37f,.55f) : new Color(.17f,.17f,.17f);
                var hover = selected ? rest : new Color(.22f,.22f,.22f);
                style.backgroundColor = rest;
                style.borderBottomColor = style.borderLeftColor = style.borderRightColor = style.borderTopColor = selected ? new Color(.55f,.75f,1f) : new Color(.28f,.28f,.28f);
                var number = new Label(setup.ToString()) { pickingMode = PickingMode.Ignore };
                number.style.fontSize = 10; number.style.unityFontStyleAndWeight = FontStyle.Bold; number.style.height = 13; number.style.marginLeft = number.style.marginRight = number.style.marginTop = number.style.marginBottom = 0; number.style.paddingLeft = number.style.paddingTop = number.style.paddingBottom = 0;
                Add(number);
                var diagram = new FmSetupView(setup, true); diagram.style.flexGrow = 1; Add(diagram);
                if (selected) AddToClassList("fm-setup-selected");
                RegisterCallback<PointerEnterEvent>(_ => style.backgroundColor = hover);
                RegisterCallback<PointerLeaveEvent>(_ => style.backgroundColor = rest);
                if (!selected) this.AddManipulator(new Clickable(pick));
            }
        }

        // The diagram for the four older routings that send more than one operator to the output.
        sealed class FmRoutingView : VisualElement
        {
            readonly int algorithm;
            const float NodeY = 70;
            const float NodeHeight = 26;
            const float FeedbackY = 145;
            static float NodeX(float width, int index) => 24 + Math.Max(0, width - 56) * index / 4;
            public FmRoutingView(int algorithm)
            {
                this.algorithm = algorithm; name = "fm-routing";
                tooltip = "Arrows carry phase modulation toward their target. Carriers connect to Output. The previous summed output feeds back into Operator 1; feedback is not an operator's self-loop.";
                style.height = 174; style.minHeight = 174; style.flexShrink = 0;
                style.minWidth = 0; style.width = Length.Percent(100);
                for (int i = 0; i < 5; i++)
                {
                    bool carrier = i == 4 || FmCarriers[algorithm].Contains(i);
                    var label = Z.Text(i < 4 ? "Op " + (i + 1) : "Out", tooltip: i < 4 ? (carrier ? "Operator " + (i + 1) + " is a carrier: its signal reaches the audible output." : "Operator " + (i + 1) + " is a modulator: its signal changes the phase of the operator its blue arrow reaches.") : "Audio output: the mean of the green carrier signals. Its previous value feeds back to Operator 1.");
                    label.name = "fm-routing-node-" + i;
                    label.style.position = Position.Absolute; label.style.top = NodeY - NodeHeight / 2;
                    int node = i; float labelWidth = i < 4 ? 36 : 42; label.style.width = labelWidth; label.style.height = NodeHeight;
                    label.style.backgroundColor = carrier ? new Color(.15f,.24f,.16f) : new Color(.13f,.19f,.27f);
                    label.style.borderBottomColor = label.style.borderLeftColor = label.style.borderRightColor = label.style.borderTopColor = carrier ? FmCarrierColor : FmModulationColor;
                    label.style.borderBottomWidth = label.style.borderLeftWidth = label.style.borderRightWidth = label.style.borderTopWidth = 1;
                    RegisterCallback<GeometryChangedEvent>(e => label.style.left = NodeX(e.newRect.width, node) - labelWidth / 2);
                    label.style.unityTextAlign = TextAnchor.MiddleCenter; Add(label);
                }
                var feedback = Z.Text("Feedback", tooltip: "Previous summed output returns to Operator 1. The Output feedback slider sets its amount.");
                feedback.name = "fm-routing-feedback"; feedback.style.position = Position.Absolute;
                feedback.style.top = FeedbackY + 2; feedback.style.left = Length.Percent(40);
                feedback.style.width = 76; feedback.style.height = 20;
                feedback.style.whiteSpace = WhiteSpace.NoWrap; feedback.style.unityTextAlign = TextAnchor.MiddleCenter;
                Add(feedback);
                generateVisualContent += Paint;
            }
            void Paint(MeshGenerationContext context)
            {
                float width = contentRect.width; if (width <= 0) return;
                var painter = context.painter2D; painter.strokeColor = FmModulationColor; painter.lineWidth = 2;
                Vector2 Node(int i) => new Vector2(NodeX(width, i), NodeY);
                void Arrow(Vector2 start, Vector2 end, float lane)
                {
                    var a = start + new Vector2(0,-NodeHeight/2); var b = end + new Vector2(0,-NodeHeight/2);
                    painter.BeginPath(); painter.MoveTo(a); painter.LineTo(new Vector2(a.x,lane)); painter.LineTo(new Vector2(b.x,lane)); painter.LineTo(b); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(b + new Vector2(-4,-7)); painter.LineTo(b); painter.LineTo(b + new Vector2(4,-7)); painter.Stroke();
                }
                var edges = FmEdges[algorithm]; for (int e = 0; e < edges.Length; e += 2) Arrow(Node(edges[e]), Node(edges[e+1]), 39 - (e / 2) * 10);
                painter.strokeColor = FmCarrierColor;
                foreach (int carrier in FmCarriers[algorithm])
                {
                    var start = Node(carrier) + new Vector2(0,NodeHeight/2); var end = Node(4) + new Vector2(0,NodeHeight/2);
                    float y = 104 + carrier * 7; painter.BeginPath(); painter.MoveTo(start); painter.LineTo(new Vector2(start.x,y)); painter.LineTo(new Vector2(end.x,y)); painter.LineTo(end); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(end+new Vector2(-4,7)); painter.LineTo(end); painter.LineTo(end+new Vector2(4,7)); painter.Stroke();
                }
                painter.strokeColor = FmFeedbackColor;
                var fbStart = Node(4) + new Vector2(24,0); var fbEnd = Node(0) + new Vector2(-18,0);
                painter.BeginPath(); painter.MoveTo(fbStart); painter.LineTo(new Vector2(fbStart.x,FeedbackY)); painter.LineTo(new Vector2(fbEnd.x,FeedbackY)); painter.LineTo(fbEnd); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(fbEnd+new Vector2(-7,-4)); painter.LineTo(fbEnd); painter.LineTo(fbEnd+new Vector2(-7,4)); painter.Stroke();
            }
        }
    }
}
