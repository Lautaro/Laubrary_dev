var sb=new System.Text.StringBuilder();
var w = ZWin("ShaperWindow");
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T322.t0",0)).ToString("F2")).Append(" ");
foreach (var e in ZAll(w.rootVisualElement)) { var l=e as UnityEngine.UIElements.Label; if (l!=null && ZDrawn(l) && l.text!=null && (l.text.StartsWith("frame ")||l.text=="gap")) sb.Append("readout='").Append(l.text).Append("' "); }
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && (b.text.Contains("Play")||b.text.Contains("Pause"))) sb.Append("btn='").Append(b.text).Append("' "); }
// lit pixels of the stage texture
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
UnityEngine.UIElements.VisualElement st=null; foreach (var e in ZAll(w.rootVisualElement)) if (e.GetType().Name=="ShaperPreviewStage") st=e;
if (st!=null) { foreach (var f in st.GetType().GetFields(BFi)) if (typeof(UnityEngine.Texture2D).IsAssignableFrom(f.FieldType)) {
    var tex = f.GetValue(st) as UnityEngine.Texture2D; if (tex==null) continue;
    int lit=0; uint h=2166136261; var px=tex.GetPixels32();
    foreach (var p in px) { if (p.a>8) lit++; h=(h^(uint)(p.r*3+p.g*5+p.b*7+p.a*11))*16777619; }
    sb.Append("tex=").Append(f.Name).Append(" ").Append(tex.width).Append("x").Append(tex.height).Append(" lit=").Append(lit).Append(" hash=").Append(h.ToString("X8")).Append(" "); } }
string rep = ZAudit(w, "shaper-playing");
sb.Append("captionShort=").Append(ZCount["captionShort"]).Append(" overflowX=").Append(ZCount["overflowParentX"]);
return sb.ToString();
