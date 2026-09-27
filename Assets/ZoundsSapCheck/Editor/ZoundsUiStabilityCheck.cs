// Proves the two layout-stability rules hold for the chain analyser, by measurement rather than by looking at it once.
//
// The rules: an element whose text may be absent, short or long must not change the height of what it sits in, and a state
// change must not resize the panel underneath a reader who may be scrolled into it. Both are about a NUMBER — the total
// height the panel occupies — so both can be checked by drawing the panel in each state and recording that number. Eyeballing
// a single screenshot cannot catch these: the bug only shows as a jump BETWEEN two states, and by the time a human sees the
// second state the first is gone.
//
// It draws the panel for real, one state per repaint, at two very different widths. Two widths matter because the failure
// mode being guarded against is wrapped text: a sentence that fits one line in a wide panel takes three in a narrow one, so
// a wrapping label makes the height width-dependent and nothing at a single width would reveal it.
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.EditorTools;

public class ZoundsUiStabilityCheck : EditorWindow {

    [MenuItem("Laubrary/Zounds/Checks/11 UI layout stability (analyser panel)")]
    public static void Open() {
        var w = GetWindow<ZoundsUiStabilityCheck>("UI stability");
        w.minSize = new Vector2(560f, 420f);
        w.Reset();
        w.Show();
    }

    struct Case {
        public string name;
        public ChainAnalyserPanel.View view;
        public bool touchChain;   // forces the transient "re-measuring" status line
        public bool disableNode;  // an effect switched off shortens its roster line
        public float width;
    }

    readonly ChainAnalyserPanel panel = new ChainAnalyserPanel();
    readonly List<Case> cases = new List<Case>();
    readonly Dictionary<string, float> measured = new Dictionary<string, float>();
    ZoundEffectChain chain;
    Klip subject;
    int cursor;
    string report = "running…";

    void Reset() {
        panel.open = true;
        cases.Clear();
        measured.Clear();
        cursor = 0;
        report = "running…";

        // A chain with one effect from each fidelity verdict, so the roster lines run from the shortest explanation to the
        // longest. If wrapping were still in play these would disagree the most.
        chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.LowPass));   // "shown exactly", short
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Phaser));    // "shown, and it moves", shortest
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Compressor));// level-dependent, long
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Distortion));// misrepresented, longest by far
        chain.nodes[0].p[0] = 1200f;

        // A throwaway id: this klip is never added to the project, it only gives the panel something to be handed.
        subject = new Klip(-999) { name = "ui-stability-probe" };

        foreach (float w in new[] { 520f, 300f }) {
            cases.Add(new Case { name = "combined settled", view = ChainAnalyserPanel.View.Combined, width = w });
            cases.Add(new Case { name = "combined re-measuring", view = ChainAnalyserPanel.View.Combined, touchChain = true, width = w });
            cases.Add(new Case { name = "combined effect off", view = ChainAnalyserPanel.View.Combined, disableNode = true, width = w });
            cases.Add(new Case { name = "live spectrum", view = ChainAnalyserPanel.View.LiveSpectrum, width = w });
            cases.Add(new Case { name = "live over time", view = ChainAnalyserPanel.View.LiveOverTime, width = w });
            cases.Add(new Case { name = "live waveform", view = ChainAnalyserPanel.View.LiveWaveform, width = w });
        }
    }

    // Drives itself to completion instead of waiting for the mouse to move. One case is measured per repaint, and a
    // background editor may not repaint at all on its own, so the run would otherwise stall half-finished.
    void OnEnable() { EditorApplication.update += Pump; }
    void OnDisable() { EditorApplication.update -= Pump; }
    void Pump() { if (cursor < cases.Count) Repaint(); }

    void OnGUI() {
        if (chain == null) Reset();

        EditorGUILayout.HelpBox(report, MessageType.None);
        if (GUILayout.Button("Run again")) Reset();

        if (cursor >= cases.Count) { DrawStill(); return; }

        var c = cases[cursor];
        panel.view = c.view;
        chain.nodes[3].enabled = !c.disableNode;
        if (c.touchChain) chain.Touch();

        // The panel is drawn inside a fixed-width area so the "narrow" case is genuinely narrow regardless of the window.
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(c.width))) {
            float before = GUILayoutUtility.GetRect(1f, 1f).yMax;
            panel.Draw(subject, chain, 120f);
            float after = GUILayoutUtility.GetRect(1f, 1f).yMax;
            if (Event.current.type == EventType.Repaint) {
                measured[c.name + " @" + c.width.ToString("0")] = after - before;
                cursor++;
                Repaint();
            }
        }
        if (cursor >= cases.Count) Compose();
    }

    void DrawStill() {
        // Leaves the last state on screen so the panel can also be looked at, not only measured.
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(520f))) panel.Draw(subject, chain, 120f);
    }

    void Compose() {
        var sb = new StringBuilder();
        var perWidth = new Dictionary<float, List<KeyValuePair<string, float>>>();
        foreach (var kv in measured) {
            float w = kv.Key.EndsWith("@300") ? 300f : 520f;
            if (!perWidth.TryGetValue(w, out var list)) perWidth[w] = list = new List<KeyValuePair<string, float>>();
            list.Add(kv);
        }

        bool ok = true;
        foreach (var group in perWidth) {
            float first = group.Value[0].Value;
            sb.Append("width ").Append(group.Key.ToString("0")).Append(":\n");
            foreach (var kv in group.Value) {
                bool same = Mathf.Abs(kv.Value - first) < 0.51f;
                if (!same) ok = false;
                sb.Append(same ? "   ok   " : "  JUMP  ").Append(kv.Key.PadRight(30))
                  .Append(kv.Value.ToString("0.0")).Append(" px\n");
            }
        }
        sb.Append(ok ? "PASS — every state occupies the same height at each width.\n"
                     : "FAIL — a state change resizes the panel; see the JUMP rows.\n");
        report = sb.ToString();
        ZoundsUiStabilityReport.last = report;
    }
}

/// <summary>Holds the last report so it can be read without a human reading the window.</summary>
public static class ZoundsUiStabilityReport {
    public static string last = "not run";
}
