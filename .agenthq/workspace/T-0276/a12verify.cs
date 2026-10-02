using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using Laubrary.Shaper;
using Laubrary.Shaper.Editor;

// A12 — read the SHIPPED card back after the fixes: which controls are disabled, and with what reason.
public static class A12Verify
{
    const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy;
    static void W(VisualElement e, List<VisualElement> l) { if (e == null) return; l.Add(e); for (int i = 0; i < e.childCount; i++) W(e[i], l); }
    static ShaperWindow Win() => EditorWindow.GetWindow<ShaperWindow>();
    static List<VisualElement> Tree() { var l = new List<VisualElement>(); W(Win().rootVisualElement, l); return l; }
    static string Txt(VisualElement v) { var p = v.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public); if (p != null && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(v) ?? ""; } catch { } } return ""; }
    static void Rebuild() { var w = Win(); w.GetType().GetMethod("Rebuild", BF).Invoke(w, null); w.Repaint(); }
    static ShaperDocument Doc()
    {
        var w = Win(); PropertyInfo cur = null;
        for (var t = w.GetType(); t != null && cur == null; t = t.BaseType) cur = t.GetProperty("Current", BF);
        return cur.GetValue(w) as ShaperDocument;
    }

    // report on one caption: is it drawn, is it enabled, what reason does it carry
    static string State(string caption)
    {
        var lab = Tree().FirstOrDefault(v => Txt(v) == caption);
        if (lab == null) return caption + ": ABSENT";
        // enabledInHierarchy on the LABEL itself already reflects any disabled ancestor
        bool en = lab.enabledInHierarchy;
        string tip = lab.tooltip;
        for (var p = lab.parent; p != null && string.IsNullOrEmpty(tip); p = p.parent) tip = p.tooltip;
        return caption + ": DRAWN enabled=" + en + "  reason=\""
             + (tip == null ? "" : (tip.Length > 130 ? tip.Substring(0, 130) + "…" : tip)) + "\"";
    }

    public static string Execute(Newtonsoft.Json.Linq.JObject a)
    {
        var sb = new StringBuilder();
        string mode = (string)a["mode"];
        var doc = Doc();
        if (doc == null) return "no document bound";
        var node = doc.layers[0].root;

        if (mode == "text")
        {
            node.kind = ShaperNodeKind.Primitive;
            node.primitive.kind = ShaperPrimitiveKind.Text;
            node.primitive.textString = "TEXT";
            Rebuild();
            sb.Append("— Text, one line —\n  ").Append(State("Line spacing")).Append("\n  ").Append(State("Align")).Append('\n');
            node.primitive.textString = "AB\nCD";
            Rebuild();
            sb.Append("— Text, two lines —\n  ").Append(State("Line spacing")).Append("\n  ").Append(State("Align")).Append('\n');
            node.primitive.textString = "TEXT";
            Rebuild();
        }
        else if (mode == "posterise")
        {
            node.kind = ShaperNodeKind.Primitive;
            node.primitive.kind = ShaperPrimitiveKind.Ellipse;
            foreach (var k in new[] { ShaperFillKind.Solid, ShaperFillKind.Gradient, ShaperFillKind.HeightField,
                                      ShaperFillKind.OverPhase, ShaperFillKind.Procedural })
            {
                node.fill.kind = k; Rebuild();
                sb.Append(k).Append("  ").Append(State("Posterise")).Append('\n');
            }
            node.fill.kind = ShaperFillKind.Solid; Rebuild();
        }
        else if (mode == "combine")
        {
            node.kind = ShaperNodeKind.Bag;
            node.children.Clear();
            var mk = typeof(ShaperWindow).GetMethod("NewBagMember", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            node.children.Add((ShaperNode)mk.Invoke(null, new object[] { "Member 1", doc }));
            var m2 = (ShaperNode)mk.Invoke(null, new object[] { "Member 2", doc });
            node.children.Add(m2);
            var w = Win();
            // drill into member 2, the way the card's own child list does
            var drill = w.GetType().GetField("drillPath", BF);
            var dp = drill.GetValue(w) as System.Collections.IList;
            dp.Clear(); dp.Add(1);
            foreach (var cm in new[] { ShaperCombineMode.Add, ShaperCombineMode.Subtract, ShaperCombineMode.Intersect })
            {
                m2.mode = cm; Rebuild();
                sb.Append("— member 2 = ").Append(cm).Append(" —\n  ")
                  .Append(State("Blend width")).Append("\n  ")
                  .Append(State("Sharpness")).Append("\n  ")
                  .Append(State("Carve strength")).Append('\n');
            }
            dp.Clear();
            node.kind = ShaperNodeKind.Primitive; node.children.Clear(); Rebuild();
        }
        else if (mode == "captions")
        {
            Rebuild();
            var seen = new Dictionary<string, int>();
            foreach (var v in Tree())
            {
                if (v.resolvedStyle.display == DisplayStyle.None) continue;
                if (v.resolvedStyle.width <= 1 || v.resolvedStyle.height <= 1) continue;
                var t = Txt(v);
                if (string.IsNullOrEmpty(t) || t.Length > 30) continue;
                bool childHasText = false;
                for (int i = 0; i < v.childCount; i++) { var all = new List<VisualElement>(); W(v[i], all); foreach (var s in all) if (Txt(s) == t) childHasText = true; }
                if (childHasText) continue;
                seen[t] = seen.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            foreach (var kv in seen.Where(k => k.Value > 1).OrderBy(k => k.Key))
                sb.Append("REPEATED \"").Append(kv.Key).Append("\" x").Append(kv.Value).Append('\n');
            sb.Append("distinct captions on screen = ").Append(seen.Count).Append('\n');
        }
        return sb.ToString();
    }
}
