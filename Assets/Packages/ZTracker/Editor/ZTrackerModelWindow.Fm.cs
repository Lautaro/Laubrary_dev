using System;
using System.Linq;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // Mirrors the six routes implemented by the native renderer. All four operators participate.
        static readonly int[][] FmEdges = { new[] { 0,1, 1,2, 2,3 }, new[] { 0,1, 2,3 }, new[] { 0,2, 1,2, 2,3 }, new[] { 0,1 }, new[] { 0,1, 1,2 }, new int[0] };
        static readonly int[][] FmCarriers = { new[] { 3 }, new[] { 1,3 }, new[] { 3 }, new[] { 1,2,3 }, new[] { 2,3 }, new[] { 0,1,2,3 } };
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

        void BuildFm(VisualElement root, InstrumentParameters q)
        {
            int algorithm = EffectiveFmAlgorithm(q.fmAlgorithm);
            var selector = Flow(InstrumentChoice("Algorithm", q.fmAlgorithm < 6 ? q.fmAlgorithm : -1, new[] { "Stack", "Pairs", "Merge", "Three carriers", "Split stack", "Parallel" }, "Directed arrows show phase modulation. Carriers reach Output; modulators shape another operator. Every operator participates in every algorithm.", v => q.fmAlgorithm = v, "fm-algorithm"));
            if (q.fmAlgorithm >= 6) selector.Add(Diagnostic("Imported routing", "Stored algorithm " + q.fmAlgorithm + " uses the Parallel fallback until explicitly changed."));
            selector.Add(ParameterValue("fmFeedback", q.fmFeedback, v => q.fmFeedback = v, "synth-fmFeedback", q, "fmFeedback")); root.Add(selector);
            if (q.fmOperators == null || q.fmOperators.Length < 4)
            {
                root.Add(Flow(Button("Create operators", "Add the missing FM operators using the declared synth defaults. Existing operators and their settings stay unchanged; Undo restores the earlier array.", () => InstrumentEdit("create FM operators", () => EnsureFmOperators(q), true), "fm-create-operators"),
                    Diagnostic("Operators missing", "This older instrument contains " + (q.fmOperators?.Length ?? 0) + " of the four FM operators. Create the missing operators to edit or play FM.")));
                return;
            }
            if (q.fmOperators.Length > 4) root.Add(Diagnostic("Extra operators retained", "The first four operators are editable and play. The extra imported operator data remains unchanged in the asset."));
            root.Add(new FmRoutingView(algorithm));
            var operators = InstrumentColumns(root, "fm-operators", 380);
            for (int i = 0; i < 4; i++)
            {
                int at = i; var op = q.fmOperators[i]; bool carrier = FmCarriers[algorithm].Contains(i);
                var box = InstrumentSection(operators, "Operator " + (i + 1) + (carrier ? " - Carrier" : " - Modulator"), "fm." + i, carrier ? "This sine operator reaches the audible output. Its level and envelope control loudness." : "This sine operator changes the phase of the operator its arrow points to. Its level and envelope control the timbre.");
                if (op.waveform != 0) box.Add(Diagnostic("Sine playback", "The imported waveform " + op.waveform + " is preserved; the runtime renders sine for FM operators."));
                var row = Flow(); VisualElement ratioControl = null;
                bool fixedEnvelope = q.GetParameterEnvelope("fmOperators." + at + ".freqFixed")?.enabled == true;
                // A fixed-frequency curve can cross zero, in which case the ratio still matters.
                foreach (string field in new[] { "freqRatio", "freqFixed", "level" })
                {
                    var f = typeof(ZTrackerInstrument.FMOperatorData).GetField(field); string id = "fmOperators." + at + "." + field;
                    var control = ParameterValue(field, (float)f.GetValue(op), v => { object next = q.fmOperators[at]; f.SetValue(next, v); q.fmOperators[at] = (ZTrackerInstrument.FMOperatorData)next; if (field == "freqFixed" && ratioControl != null) ratioControl.style.display = v > 0 && q.GetParameterEnvelope("fmOperators." + at + ".freqFixed")?.enabled != true ? DisplayStyle.None : DisplayStyle.Flex; }, id, q, id);
                    if (field == "freqRatio") { ratioControl = control; control.style.display = op.freqFixed > 0 && !fixedEnvelope ? DisplayStyle.None : DisplayStyle.Flex; }
                    if (field == "freqFixed") control.Q<ZuiValueControl>().ModeChanged += () => control.schedule.Execute(() => { ratioControl.style.display = q.fmOperators[at].freqFixed > 0 && q.GetParameterEnvelope("fmOperators." + at + ".freqFixed")?.enabled != true ? DisplayStyle.None : DisplayStyle.Flex; });
                    row.Add(control);
                }
                box.Add(row);
                float Read(string field) => (float)typeof(ZTrackerInstrument.FMOperatorData).GetField(field).GetValue(q.fmOperators[at]);
                void Write(string field, float value) => InstrumentEdit("operator " + field, () => { object next = q.fmOperators[at]; typeof(ZTrackerInstrument.FMOperatorData).GetField(field).SetValue(next, value); q.fmOperators[at] = (ZTrackerInstrument.FMOperatorData)next; });
                box.Add(Named(Z.Adsr("Amplitude", () => Read("attack"), v => Write("attack",v), () => Read("decay"), v => Write("decay",v), () => Read("sustain"), v => Write("sustain",v), () => Read("release"), v => Write("release",v), "Fixed amplitude stages; each note restarts this operator's envelope. Double-click a stage to reset.", EnvelopeDefaults(NewInstrumentData().parameters.fmOperators[at])), "fm-adsr-" + at));
            }
        }

        sealed class FmRoutingView : VisualElement
        {
            readonly int algorithm;
            const float NodeY = 70;
            const float NodeHeight = 26;
            const float FeedbackY = 145;
            static readonly Color ModulationColor = new Color(.45f,.7f,.95f);
            static readonly Color CarrierColor = new Color(.5f,.85f,.55f);
            static readonly Color FeedbackColor = new Color(.8f,.6f,.9f);
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
                    label.style.borderBottomColor = label.style.borderLeftColor = label.style.borderRightColor = label.style.borderTopColor = carrier ? CarrierColor : ModulationColor;
                    label.style.borderBottomWidth = label.style.borderLeftWidth = label.style.borderRightWidth = label.style.borderTopWidth = 1;
                    RegisterCallback<GeometryChangedEvent>(e => label.style.left = NodeX(e.newRect.width, node) - labelWidth / 2);
                    label.style.unityTextAlign = TextAnchor.MiddleCenter; Add(label);
                }
                var feedback = Z.Text("Feedback", tooltip: "Previous summed output returns to Operator 1. The Feedback slider sets its amount.");
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
                var painter = context.painter2D; painter.strokeColor = ModulationColor; painter.lineWidth = 2;
                Vector2 Node(int i) => new Vector2(NodeX(width, i), NodeY);
                void Arrow(Vector2 start, Vector2 end, float lane)
                {
                    var a = start + new Vector2(0,-NodeHeight/2); var b = end + new Vector2(0,-NodeHeight/2);
                    painter.BeginPath(); painter.MoveTo(a); painter.LineTo(new Vector2(a.x,lane)); painter.LineTo(new Vector2(b.x,lane)); painter.LineTo(b); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(b + new Vector2(-4,-7)); painter.LineTo(b); painter.LineTo(b + new Vector2(4,-7)); painter.Stroke();
                }
                var edges = FmEdges[algorithm]; for (int e = 0; e < edges.Length; e += 2) Arrow(Node(edges[e]), Node(edges[e+1]), 39 - (e / 2) * 10);
                painter.strokeColor = CarrierColor;
                foreach (int carrier in FmCarriers[algorithm])
                {
                    var start = Node(carrier) + new Vector2(0,NodeHeight/2); var end = Node(4) + new Vector2(0,NodeHeight/2);
                    float y = 104 + carrier * 7; painter.BeginPath(); painter.MoveTo(start); painter.LineTo(new Vector2(start.x,y)); painter.LineTo(new Vector2(end.x,y)); painter.LineTo(end); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(end+new Vector2(-4,7)); painter.LineTo(end); painter.LineTo(end+new Vector2(4,7)); painter.Stroke();
                }
                painter.strokeColor = FeedbackColor;
                var fbStart = Node(4) + new Vector2(24,0); var fbEnd = Node(0) + new Vector2(-18,0);
                painter.BeginPath(); painter.MoveTo(fbStart); painter.LineTo(new Vector2(fbStart.x,FeedbackY)); painter.LineTo(new Vector2(fbEnd.x,FeedbackY)); painter.LineTo(fbEnd); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(fbEnd+new Vector2(-7,-4)); painter.LineTo(fbEnd); painter.LineTo(fbEnd+new Vector2(-7,4)); painter.Stroke();
            }
        }
    }
}
