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
        static VisualElement InstrumentColumns(VisualElement root, string name, float columnMinimum)
        {
            var row = new VisualElement { name = name };
            row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            row.style.minWidth = 0; row.style.width = Length.Percent(100); row.style.alignItems = Align.FlexStart;
            root.Add(row);
            void Layout(float ignored)
            {
                float width = row.contentRect.width; if (width <= 0) return;
                int count = Math.Max(1, Math.Min(row.childCount, (int)(width / columnMinimum)));
                foreach (var child in row.Children())
                {
                    child.style.minWidth = 0; child.style.flexGrow = 0; child.style.flexShrink = 1;
                    float divisor = child.ClassListContains("tracker-instrument-wide") ? 1 : count;
                    float margins = child.resolvedStyle.marginLeft + child.resolvedStyle.marginRight;
                    float childWidth = Math.Max(0, (float)Math.Floor((width - 1) / divisor - (float.IsNaN(margins) ? 0 : margins)));
                    child.style.width = childWidth; child.style.maxWidth = childWidth;
                }
            }
            row.RegisterCallback<GeometryChangedEvent>(e => Layout(e.newRect.width));
            row.RegisterCallback<AttachToPanelEvent>(_ => row.schedule.Execute(() => Layout(row.contentRect.width)));
            return row;
        }

        void RefreshSynthConditionalControls(VisualElement root)
        {
            var q = Instrument?.parameters; if (q == null) return;
            void Show(string name, bool active)
            {
                var control = root.Q<VisualElement>(name);
                if (control != null) { var host = control is ZuiValueControl ? control.parent : control; host.style.display = active ? DisplayStyle.Flex : DisplayStyle.None; }
            }
            Show("synth-unisonDetune", q.unisonVoices > 1); Show("synth-unisonSpread", q.unisonVoices > 1);
        }

        void BuildRetainedInstrumentEffects(VisualElement root)
        {
            var q = Instrument.parameters;
            if (q.instFilterEnabled) BuildInstrumentFilter(root, q, "retained.filter", "retained");
            bool HasSend(Laubrary.Zounds.ZoundEffectType type) => Data.tracks.Any(t => t != null && t.kind == TrackKind.Send && t.devices?.nodes != null && t.devices.nodes.Any(n => n != null && n.type == type));
            bool supportsSends = Instrument.family == InstrumentFamily.Synth || Instrument.provenance == "native-v0" || Instrument.provenance?.StartsWith("native-v0;", StringComparison.Ordinal) == true;
            bool delay = supportsSends && HasSend(Laubrary.Zounds.ZoundEffectType.Delay), reverb = supportsSends && HasSend(Laubrary.Zounds.ZoundEffectType.Reverb);
            if (delay || reverb || q.instDelaySend != 0 || q.instReverbSend != 0)
            {
                var sends = InstrumentSection(root, "Mixer sends", "mixer-sends", "These levels route to matching song Send tracks in Mixer, independently of this instrument's effect chain. Stored levels are retained when a destination is absent.");
                var row = Flow();
                if (delay) row.Add(NumberForField("instDelaySend", q.instDelaySend, v => q.instDelaySend = v, "synth-instDelaySend"));
                else if (q.instDelaySend != 0) row.Add(Diagnostic("Delay destination", supportsSends ? "The stored delay send is " + q.instDelaySend.ToString("P0") + "; playback requires a Mixer Send track containing Delay. The authored amount is preserved." : "The stored delay send is " + q.instDelaySend.ToString("P0") + "; this imported sampler does not use retained instrument sends. The amount is preserved; route it through Mixer instead."));
                if (reverb) row.Add(NumberForField("instReverbSend", q.instReverbSend, v => q.instReverbSend = v, "synth-instReverbSend"));
                else if (q.instReverbSend != 0) row.Add(Diagnostic("Reverb destination", supportsSends ? "The stored reverb send is " + q.instReverbSend.ToString("P0") + "; playback requires a Mixer Send track containing Reverb. The authored amount is preserved." : "The stored reverb send is " + q.instReverbSend.ToString("P0") + "; this imported sampler does not use retained instrument sends. The amount is preserved; route it through Mixer instead."));
                sends.Add(row);
            }
        }
    }
}
