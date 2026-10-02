var SB = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
System.Func<System.Type,string,System.Reflection.FieldInfo> F = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var f=x.GetField(n,BF); if (f!=null) return f; } return null; };
System.Func<System.Type,string,System.Reflection.MethodInfo> M = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var m=x.GetMethod(n,BF); if (m!=null) return m; } return null; };
System.Func<System.Type,string,System.Reflection.PropertyInfo> P = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var p=x.GetProperty(n,BF); if (p!=null) return p; } return null; };
System.Func<Laubrary.Shaper.Editor.ShaperWindow> Win = () => UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e==null) return; into.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) WalkT(e.hierarchy[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(Win().rootVisualElement, l); return l; };
System.Func<UnityEngine.UIElements.VisualElement,string> TextOf = v => { var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public); if (p!=null && p.PropertyType==typeof(string)) { try { return (string)p.GetValue(v); } catch {} } return null; };
System.Func<string,UnityEngine.UIElements.Button> FindBtn = s => { foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text!=null && b.text.Contains(s) && v.resolvedStyle.display!=UnityEngine.UIElements.DisplayStyle.None) return b; return null; };
System.Func<UnityEngine.UIElements.VisualElement,string,string> Click = (e,lbl) => { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = e; e.SendEvent(ev); } return lbl; };
System.Func<string,string> Press = s => { var b = FindBtn(s); if (b==null) return "NOTFOUND:"+s; bool en=b.enabledInHierarchy; Click(b,""); return "pressed \""+b.text+"\" en="+en; };
var win = Win(); var wt = win.GetType();
// clear any prior sampler
var prevKey = UnityEditor.SessionState.GetString("A22.samples", "");
UnityEditor.SessionState.SetString("A22.samples", "");
System.Func<string> Sample = () => {
  var w = Win(); var t2 = w.GetType();
  object playing = F(t2,"playing")?.GetValue(w); object fr = F(t2,"currentFrame")?.GetValue(w);
  var pv = P(t2,"previewFrame"); object pfr = pv==null?null:pv.GetValue(w);
  string hash="n/a"; int lit=-1; string bgnull="?";
  var stage = F(t2,"stage")?.GetValue(w);
  if (stage!=null) {
    var img = F(stage.GetType(),"_image")?.GetValue(stage) as UnityEngine.UIElements.VisualElement;
    if (img!=null) bgnull = (img.style.backgroundImage.value.texture==null && img.style.backgroundImage.value.sprite==null) ? "BLANK" : "shown";
    var tex = F(stage.GetType(),"_tex")?.GetValue(stage) as UnityEngine.Texture2D;
    if (tex!=null) { var px = tex.GetPixels32(); ulong h=1469598103934665603UL; lit=0;
      foreach (var c in px) { if (c.a>0) lit++; h^=c.r; h*=1099511628211UL; h^=c.g; h*=1099511628211UL; h^=c.b; h*=1099511628211UL; h^=c.a; h*=1099511628211UL; } hash=h.ToString("x16"); } }
  return System.DateTime.Now.ToString("HH:mm:ss.fff")+" play="+playing+" f="+fr+" pf="+pfr+" lit="+lit+" stage="+bgnull+" h="+hash; };
// start play through the real button
SB.Append(Press("▶ Play")).Append('\n');
SB.Append("first sample: ").Append(Sample()).Append('\n');
// schedule 8 samples 1.2 s apart on the editor update loop
double t0 = UnityEditor.EditorApplication.timeSinceStartup;
int n = 0;
UnityEditor.EditorApplication.CallbackFunction cb = null;
cb = () => {
  double el = UnityEditor.EditorApplication.timeSinceStartup - t0;
  if (el < (n+1)*1.2) return;
  n++;
  var acc = UnityEditor.SessionState.GetString("A22.samples","");
  UnityEditor.SessionState.SetString("A22.samples", acc + "#" + n + " " + Sample() + "\n");
  if (n >= 8) UnityEditor.EditorApplication.update -= cb;
};
UnityEditor.EditorApplication.update += cb;
SB.Append("sampler armed for 8 samples over ~10 s\n");
return SB.ToString();
