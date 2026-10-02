// T-0305 — measures "Appearance order" liveness against how the swarm card draws it, in all six states,
// on a SCRATCH copy of a document (Assets/Shaper/AuditT0305.asset), which is deleted by s6-clean.cs.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
const string scratch = "Assets/Shaper/AuditT0305.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(scratch) == null)
{
    UnityEditor.AssetDatabase.CopyAsset("Assets/Shaper/New Shaper.asset", scratch);
    UnityEditor.AssetDatabase.Refresh();
}
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(scratch);
sb.Append("scratch=").Append(doc == null ? "NULL" : doc.name).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset = null;
for (var t = win.GetType(); t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { doc });
System.Reflection.MethodInfo rebuild = null;
for (var t = win.GetType(); t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);

var s = doc.layers[0].root.swarm;
s.enabled = true; s.count = 8; s.EnsureDials(); s.spawnerRadius.staticValue = 24f;

System.Func<int, int[]> H = f => { var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f); var o = new int[px.Length]; for (int i = 0; i < px.Length; i++) o[i] = (px[i].r << 24) | (px[i].g << 16) | (px[i].b << 8) | px[i].a; return o; };
System.Func<int[], int[], int> D = (a, b) => { int d = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++; return d; };
System.Func<int> M = () => { int tot = 0; float keep = s.spawnOrderChaos; for (int f = 0; f < doc.frameCount; f += 4) { s.spawnOrderChaos = 0f; var b = H(f); s.spawnOrderChaos = 1f; var a = H(f); tot += D(b, a); } s.spawnOrderChaos = keep; return tot; };

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };

foreach (var sh in new[] { Laubrary.Shaper.ShaperSwarmShape.None, Laubrary.Shaper.ShaperSwarmShape.Circle })
    foreach (var ti in new[] { Laubrary.Shaper.ShaperSwarmTiming.Stagger, Laubrary.Shaper.ShaperSwarmTiming.Window, Laubrary.Shaper.ShaperSwarmTiming.FrameStep })
    {
        s.shape = sh; s.timing = ti; s.spawnOrderChaos = 0f;
        rebuild.Invoke(win, null);
        int px = M();
        var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
        string drawn = "NOT DRAWN", tip = "";
        bool enabled = false;
        foreach (var v in all)
        {
            var lb = v as UnityEngine.UIElements.Label;
            if (lb == null || lb.text != "Appearance order") continue;
            drawn = "drawn";
            enabled = lb.enabledInHierarchy;
            tip = lb.tooltip;
            break;
        }
        if (drawn == "NOT DRAWN")
            foreach (var v in all)
            {
                var ms = v as Laubrary.Zui.ZuiMicroSlider;
                if (ms == null) continue;
                if (ms.tooltip != null && ms.tooltip.Contains("neighbouring positions")) { drawn = "drawn (slider)"; enabled = ms.enabledInHierarchy; tip = ms.tooltip; break; }
            }
        sb.Append("shape=").Append(sh).Append(" timing=").Append(ti)
          .Append("  chaos 0->1 moves ").Append(px).Append(" px   UI: ").Append(drawn)
          .Append(enabled ? " ENABLED" : " greyed")
          .Append("  tip='").Append(tip.Length > 90 ? tip.Substring(0, 90) + "…" : tip).Append("'\n");
    }

s.shape = Laubrary.Shaper.ShaperSwarmShape.None; s.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger;
s.spawnOrderChaos = 0f; s.enabled = false; s.count = 5;
rebuild.Invoke(win, null);
return sb.ToString();
