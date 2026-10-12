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

        void BuildFm(VisualElement root, InstrumentParameters q)
        {
            int algorithm = EffectiveFmAlgorithm(q.fmAlgorithm);
            var selector = Flow(InstrumentChoice("Algorithm", q.fmAlgorithm < 6 ? q.fmAlgorithm : -1, new[] { "Stack", "Pairs", "Merge", "Three carriers", "Split stack", "Parallel" }, "Directed arrows show phase modulation. Carriers reach Output; modulators shape another operator. Every operator participates in every algorithm.", v => q.fmAlgorithm = v, "fm-algorithm"));
            if (q.fmAlgorithm >= 6) selector.Add(Diagnostic("Imported routing", "Stored algorithm " + q.fmAlgorithm + " uses the Parallel fallback until explicitly changed."));
            selector.Add(ParameterValue("fmFeedback", q.fmFeedback, v => q.fmFeedback = v, "synth-fmFeedback", q, "fmFeedback")); root.Add(selector);
            root.Add(new FmRoutingView(algorithm));
            if (q.fmOperators == null || q.fmOperators.Length != 4)
            {
                root.Add(Diagnostic("Operator data", "Playback requires exactly four operators. This malformed authored payload is preserved; repair its source before playback."));
                return;
            }
            var operators = InstrumentColumns(root, "fm-operators", 380);
            for (int i = 0; i < 4; i++)
            {
                int at = i; var op = q.fmOperators[i]; bool carrier = FmCarriers[algorithm].Contains(i);
                var box = InstrumentSection(operators, "Operator " + (i + 1) + (carrier ? " ? Carrier" : " ? Modulator"), "fm." + i, carrier ? "This sine operator reaches the audible output. Its level and envelope control loudness." : "This sine operator changes the phase of the operator its arrow points to. Its level and envelope control the timbre.");
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
                box.Add(Named(Z.Adsr("Amplitude", () => Read("attack"), v => Write("attack",v), () => Read("decay"), v => Write("decay",v), () => Read("sustain"), v => Write("sustain",v), () => Read("release"), v => Write("release",v), "Fixed amplitude stages; each note restarts this operator's envelope."), "fm-adsr-" + at));
            }
        }

        sealed class FmRoutingView : VisualElement
        {
            readonly int algorithm;
            public FmRoutingView(int algorithm)
            {
                this.algorithm = algorithm; name = "fm-routing";
                tooltip = "Arrows carry phase modulation toward their target. Carriers connect to Output. The previous summed output feeds back into Operator 1; feedback is not an operator's self-loop.";
                style.height = 160; style.minWidth = 0; style.width = Length.Percent(100);
                for (int i = 0; i < 5; i++)
                {
                    var label = Z.Text(i < 4 ? (i + 1).ToString() : "Output", tooltip: i < 4 ? (FmCarriers[algorithm].Contains(i) ? "Carrier: contributes directly to Output." : "Modulator: changes the phase of the connected operator.") : "The mean of the carrier signals, with output feedback sent to Operator 1.");
                    label.style.position = Position.Absolute; label.style.top = 57;
                    int node = i; float labelWidth = i < 4 ? 22 : 52; label.style.width = labelWidth;
                    RegisterCallback<GeometryChangedEvent>(e => label.style.left = 30 + Math.Max(0, e.newRect.width - 80) * node / 4 - labelWidth / 2);
                    label.style.unityTextAlign = TextAnchor.MiddleCenter; Add(label);
                }
                var feedback = Z.Text("Feedback", tooltip: "Previous summed output returns to Operator 1. The Feedback slider sets its amount.");
                feedback.style.position = Position.Absolute; feedback.style.top = 141; feedback.style.left = Length.Percent(40); Add(feedback);
                generateVisualContent += Paint;
            }
            void Paint(MeshGenerationContext context)
            {
                float width = contentRect.width; if (width <= 0) return;
                var painter = context.painter2D; painter.strokeColor = new Color(.45f,.7f,.95f); painter.lineWidth = 2;
                Vector2 Node(int i) => new Vector2(30 + Math.Max(0, width - 80) * i / 4, 67);
                void Arrow(Vector2 start, Vector2 end, float rise)
                {
                    var a = start + new Vector2(0,-13); var b = end + new Vector2(0,-13);
                    painter.BeginPath(); painter.MoveTo(a); painter.LineTo(a + Vector2.up * rise); painter.LineTo(b + Vector2.up * rise); painter.LineTo(b); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(b + new Vector2(-4,-7)); painter.LineTo(b); painter.LineTo(b + new Vector2(4,-7)); painter.Stroke();
                }
                var edges = FmEdges[algorithm]; for (int e = 0; e < edges.Length; e += 2) Arrow(Node(edges[e]), Node(edges[e+1]), -18 - (e / 2) * 10);
                painter.strokeColor = new Color(.5f,.85f,.55f);
                foreach (int carrier in FmCarriers[algorithm])
                {
                    var start = Node(carrier) + new Vector2(0,14); var end = Node(4) + new Vector2(0,14);
                    float y = 91 + carrier * 7; painter.BeginPath(); painter.MoveTo(start); painter.LineTo(new Vector2(start.x,y)); painter.LineTo(new Vector2(end.x,y)); painter.LineTo(end); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(end+new Vector2(-4,7)); painter.LineTo(end); painter.LineTo(end+new Vector2(4,7)); painter.Stroke();
                }
                painter.strokeColor = new Color(.8f,.6f,.9f);
                var fbStart = Node(4) + new Vector2(18,0); var fbEnd = Node(0) + new Vector2(-15,0);
                painter.BeginPath(); painter.MoveTo(fbStart); painter.LineTo(new Vector2(fbStart.x,135)); painter.LineTo(new Vector2(fbEnd.x,135)); painter.LineTo(fbEnd); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(fbEnd+new Vector2(-4,7)); painter.LineTo(fbEnd); painter.LineTo(fbEnd+new Vector2(4,7)); painter.Stroke();
            }
        }
    }
}
