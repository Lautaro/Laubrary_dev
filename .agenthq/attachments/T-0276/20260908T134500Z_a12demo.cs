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

// A12 — the demo document, loaded through the window's own binding, played, sampled over time.
// NOTHING here saves: the document is never written and its dirty flag is reported at the end.
public static class A12Demo
{
    const string PATH = "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset";
    const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy;
    static ShaperWindow Win() => EditorWindow.GetWindow<ShaperWindow>();
    static void W(VisualElement e, List<VisualElement> l) { if (e == null) return; l.Add(e); for (int i = 0; i < e.childCount; i++) W(e[i], l); }
    static List<VisualElement> Tree() { var l = new List<VisualElement>(); W(Win().rootVisualElement, l); return l; }

    public static string Load()
    {
        var doc = AssetDatabase.LoadAssetAtPath<ShaperDocument>(PATH);
        if (doc == null) return "demo document not found at " + PATH;
        var w = Win();
        var set = w.GetType().GetMethod("SetAsset", BF);
        set.Invoke(w, new object[] { doc });
        w.titleContent = new GUIContent("ShaperCapTag");
        w.Repaint();
        var sb = new StringBuilder();
        sb.Append("loaded ").Append(doc.name).Append("  layers=").Append(doc.layers.Count)
          .Append(" frames=").Append(doc.frameCount).Append(" canvas=").Append(doc.canvasWidth).Append('x').Append(doc.canvasHeight)
          .Append("  dirty=").Append(EditorUtility.IsDirty(doc)).Append('\n');
        // render every frame through the engine, so a blank or throwing frame cannot hide
        int blank = 0; var lits = new List<int>();
        for (int f = 0; f < doc.frameCount; f++)
        {
            var px = ShaperDocumentRenderer.RenderFrame(doc, f);
            int lit = 0; foreach (var c in px) if (c.a > 0) lit++;
            lits.Add(lit); if (lit == 0) blank++;
        }
        sb.Append("frames lit: ").Append(string.Join(",", lits)).Append("  blank frames=").Append(blank).Append('\n');
        return sb.ToString();
    }

    public static string Play()
    {
        var b = Tree().OfType<Button>().FirstOrDefault(x => x.text != null && (x.text.Contains("Play") || x.text.Contains("Pause")));
        if (b == null) return "no transport button";
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        var w = Win();
        return "pressed \"" + b.text + "\" -> playing=" + w.GetType().GetField("playing", BF).GetValue(w);
    }

    public static string Sample()
    {
        var w = Win();
        var stage = w.GetType().GetField("stage", BF)?.GetValue(w);
        var tex = stage?.GetType().GetField("_tex", BF)?.GetValue(stage) as Texture2D;
        int lit = -1; string hash = "n/a";
        if (tex != null)
        {
            var px = tex.GetPixels32(); lit = 0; ulong h = 1469598103934665603UL;
            foreach (var c in px) { if (c.a > 0) lit++; h ^= c.r; h *= 1099511628211UL; h ^= c.g; h *= 1099511628211UL; h ^= c.b; h *= 1099511628211UL; h ^= c.a; h *= 1099511628211UL; }
            hash = h.ToString("x12");
        }
        PropertyInfo cur = null; for (var t = w.GetType(); t != null && cur == null; t = t.BaseType) cur = t.GetProperty("Current", BF);
        var doc = cur.GetValue(w) as ShaperDocument;
        return DateTime.Now.ToString("HH:mm:ss.fff")
             + "  playing=" + w.GetType().GetField("playing", BF).GetValue(w)
             + " frame=" + w.GetType().GetField("currentFrame", BF).GetValue(w)
             + " stageLit=" + lit + " hash=" + hash
             + " docDirty=" + (doc == null ? "n/a" : EditorUtility.IsDirty(doc).ToString());
    }

    public static string Stop()
    {
        var b = Tree().OfType<Button>().FirstOrDefault(x => x.text != null && x.text.Contains("Pause"));
        if (b == null) return "not playing (no Pause button)";
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        var w = Win();
        var doc = AssetDatabase.LoadAssetAtPath<ShaperDocument>(PATH);
        return "paused -> playing=" + w.GetType().GetField("playing", BF).GetValue(w)
             + "  demo dirty=" + EditorUtility.IsDirty(doc);
    }
}
