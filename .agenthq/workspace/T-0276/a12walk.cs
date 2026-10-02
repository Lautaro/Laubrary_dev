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

// A12 (T-0276) — cold walk of the Shaper window: empty state, twice, temporal, on a NEW document and on
// the demo document. Every entry point is driven the way a person drives it (a real click event on the
// real button), never by poking the field behind it.
public static class A12Walk
{
    const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    static FieldInfo F(Type t, string n) { for (var x = t; x != null; x = x.BaseType) { var f = x.GetField(n, BF); if (f != null) return f; } return null; }
    static MethodInfo M(Type t, string n) { for (var x = t; x != null; x = x.BaseType) { var m = x.GetMethod(n, BF); if (m != null) return m; } return null; }
    static PropertyInfo P(Type t, string n) { for (var x = t; x != null; x = x.BaseType) { var p = x.GetProperty(n, BF); if (p != null) return p; } return null; }

    static ShaperWindow Win() => EditorWindow.GetWindow<ShaperWindow>();

    // ── the window's own tree, flattened ─────────────────────────────────────────────────────────────
    static void Walk(VisualElement e, List<VisualElement> into)
    {
        if (e == null) return;
        into.Add(e);
        for (int i = 0; i < e.childCount; i++) Walk(e[i], into);
    }
    static List<VisualElement> Tree()
    {
        var l = new List<VisualElement>();
        Walk(Win().rootVisualElement, l);
        return l;
    }
    static string TextOf(VisualElement v)
    {
        var p = v.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        if (p != null && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(v); } catch { } }
        return null;
    }
    static string PathOf(VisualElement v)
    {
        var s = new List<string>();
        for (var p = v; p != null; p = p.parent)
        {
            var t = TextOf(p);
            s.Add(p.GetType().Name + (string.IsNullOrEmpty(t) ? "" : "[" + t + "]"));
            if (s.Count > 6) break;
        }
        s.Reverse();
        return string.Join("/", s);
    }

    // ── 1. the empty state ───────────────────────────────────────────────────────────────────────────
    public static string Cold()
    {
        var sb = new StringBuilder();
        // close every instance so the next Open is genuinely a first open
        foreach (var w in Resources.FindObjectsOfTypeAll<ShaperWindow>().ToArray()) w.Close();
        EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
        var win = Win();
        win.position = new Rect(80, 60, 1500, 1200);
        win.titleContent = new GUIContent("ShaperCapTag");
        win.Show(); win.Repaint();
        var cur = P(win.GetType(), "Current");
        var doc = cur == null ? null : cur.GetValue(win) as ShaperDocument;
        sb.Append("opened; bound document = ").Append(doc == null ? "<none>" : doc.name).Append('\n');
        var tree = Tree();
        sb.Append("visual elements = ").Append(tree.Count).Append('\n');
        var buttons = tree.Where(v => v is Button).Select(v => ((Button)v).text).Where(s => !string.IsNullOrEmpty(s)).ToList();
        sb.Append("buttons: ").Append(string.Join(" | ", buttons)).Append('\n');
        return sb.ToString();
    }

    // ── 2. every caption on screen at once, and any that repeats ─────────────────────────────────────
    public static string Captions()
    {
        var sb = new StringBuilder();
        var tree = Tree();
        // a caption is a Label or a control's own text; count only elements that are actually laid out
        var seen = new Dictionary<string, List<string>>();
        foreach (var v in tree)
        {
            if (v.resolvedStyle.display == DisplayStyle.None) continue;
            var t = TextOf(v);
            if (string.IsNullOrEmpty(t)) continue;
            if (t.Length > 32) continue;                       // a paragraph is not a caption
            // skip labels that are a control's VALUE readout rather than its name
            bool numeric = t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '+');
            if (numeric) continue;
            // a MicroSlider draws its caption as a child Label of the slider; take the deepest text only
            bool hasTextChild = false;
            for (int i = 0; i < v.childCount; i++) if (!string.IsNullOrEmpty(TextOf(v[i]))) hasTextChild = true;
            if (hasTextChild) continue;
            if (!seen.TryGetValue(t, out var l)) seen[t] = l = new List<string>();
            l.Add(PathOf(v));
        }
        foreach (var kv in seen.OrderBy(k => k.Key))
            if (kv.Value.Count > 1)
            {
                sb.Append("DUPLICATE \"").Append(kv.Key).Append("\" x").Append(kv.Value.Count).Append('\n');
                foreach (var p in kv.Value.Take(4)) sb.Append("      ").Append(p).Append('\n');
            }
        sb.Append("distinct captions on screen = ").Append(seen.Count).Append('\n');
        return sb.ToString();
    }

    // ── 3. drive the Play button the way a person does ───────────────────────────────────────────────
    static Button FindButton(string containing)
    {
        foreach (var v in Tree())
            if (v is Button b && b.text != null && b.text.Contains(containing)) return b;
        return null;
    }
    public static string ClickPlay()
    {
        var b = FindButton("Play");
        if (b == null) b = FindButton("Pause");
        if (b == null) return "NO PLAY BUTTON";
        bool en = b.enabledInHierarchy;
        using (var ev = NavigationSubmitEvent.GetPooled())
        {
            ev.target = b;
            b.SendEvent(ev);
        }
        var win = Win();
        var playing = F(win.GetType(), "playing");
        return "clicked \"" + b.text + "\" (enabled=" + en + ") -> playing=" + playing.GetValue(win)
               + " buttonNow=\"" + b.text + "\"";
    }
    public static string ClickButton(Newtonsoft.Json.Linq.JObject a)
    {
        var b = FindButton((string)a["text"]);
        if (b == null) return "NOT FOUND: " + (string)a["text"];
        bool en = b.enabledInHierarchy;
        using (var ev = NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); }
        return "clicked \"" + (string)a["text"] + "\" enabled=" + en;
    }

    // a temporal sample: what the transport reads and what the preview actually shows
    public static string Sample()
    {
        var win = Win();
        var fr = F(win.GetType(), "currentFrame");
        var playing = F(win.GetType(), "playing");
        var cur = P(win.GetType(), "Current");
        var doc = cur.GetValue(win) as ShaperDocument;
        // the preview stage's own texture, hashed — this is what is on screen, not what the model says
        string hash = "n/a";
        int lit = -1;
        var stageF = F(win.GetType(), "stage");
        var stage = stageF?.GetValue(win);
        if (stage != null)
        {
            var texF = F(stage.GetType(), "_tex");
            var tex = texF?.GetValue(stage) as Texture2D;
            if (tex != null)
            {
                var px = tex.GetPixels32();
                ulong h = 1469598103934665603UL;
                lit = 0;
                foreach (var c in px) { if (c.a > 0) lit++; h ^= c.r; h *= 1099511628211UL; h ^= c.g; h *= 1099511628211UL; h ^= c.b; h *= 1099511628211UL; h ^= c.a; h *= 1099511628211UL; }
                hash = h.ToString("x16");
            }
        }
        return "t=" + DateTime.Now.ToString("HH:mm:ss.fff")
             + " playing=" + playing.GetValue(win)
             + " frame=" + fr.GetValue(win)
             + " doc=" + (doc == null ? "<none>" : doc.name)
             + " stageLit=" + lit + " stageHash=" + hash;
    }

    public static string Snap(Newtonsoft.Json.Linq.JObject a)
    {
        EditorPrefs.SetString("ShaperCap.name", (string)a["name"]);
        var w = Win();
        w.titleContent = new GUIContent("ShaperCapTag");
        w.Repaint();
        return "tagged " + (string)a["name"];
    }
}
