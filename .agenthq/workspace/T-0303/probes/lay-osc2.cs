System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var old = UnityEditor.SessionState.GetString("A25.snap", "").Split(';');
var sb = new System.Text.StringBuilder();
sb.Append("elements now=").Append(all.Count).Append(" then=").Append(old.Length - 1).Append("\n");
int diff = 0;
for (int i = 0; i < all.Count && i < old.Length - 1; i++)
{
    var p = old[i].Split('=')[1].Split(',');
    var r = all[i].worldBound;
    if (System.Math.Abs(float.Parse(p[0]) - r.x) > 0.5f || System.Math.Abs(float.Parse(p[1]) - r.y) > 0.5f ||
        System.Math.Abs(float.Parse(p[2]) - r.width) > 0.5f || System.Math.Abs(float.Parse(p[3]) - r.height) > 0.5f)
    {
        diff++;
        if (diff <= 10)
        {
            var chain = new System.Text.StringBuilder();
            for (var q = all[i]; q != null && chain.Length < 160; q = q.parent) { chain.Append(q.GetType().Name); foreach (var c in q.GetClasses()) if (c.StartsWith("zui-")) { chain.Append('.').Append(c); break; } chain.Append(" < "); }
            sb.Append("  MOVED [").Append(i).Append("] ").Append(old[i].Split('=')[1]).Append(" -> ").Append(r.x.ToString("F1")).Append(',').Append(r.y.ToString("F1")).Append(',').Append(r.width.ToString("F1")).Append(',').Append(r.height.ToString("F1"))
              .Append("  text='").Append(all[i] is UnityEngine.UIElements.Label lb ? lb.text : (all[i] is UnityEngine.UIElements.Button bt ? bt.text : "")).Append("'\n    ").Append(chain).Append("\n");
        }
    }
}
sb.Append("elements whose rect changed while idle = ").Append(diff).Append("\n");
return sb.ToString();
