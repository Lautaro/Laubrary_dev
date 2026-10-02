// After a data edit made through the window's own control, does the PREVIEW show what the document says?
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null, stageF=null, frameF=null;
for (var t=win.GetType(); t!=null; t=t.BaseType){ if(assetF==null) assetF=t.GetField("asset",BFi); if(stageF==null) stageF=t.GetField("stage",BFi); if(frameF==null) frameF=t.GetField("currentFrame",BFi); }
var doc = assetF.GetValue(win);
var bakerT = ZType("ShaperBaker");
var render = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<UnityEngine.Color32[],string> H = px => { unchecked { uint h=2166136261u; foreach (var p in px){h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u;} return h.ToString("X8"); } };
var stage = stageF.GetValue(win);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
System.Func<string> cmp = () => { var s=new System.Text.StringBuilder();
  foreach (int f in new int[]{0,4,12}) {
    string hr = H(render.Invoke(null, new object[]{doc,f}) as UnityEngine.Color32[]);
    frameF.SetValue(win, f); refresh.Invoke(stage, null);
    var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
    string hs = tex==null?"notex":H(tex.GetPixels32());
    s.Append("f").Append(f).Append(" renderer=").Append(hr).Append(" stage=").Append(hs).Append(hr==hs?" ok":" STALE").Append("\n"); }
  return s.ToString(); };
var sb = new System.Text.StringBuilder();
sb.Append("-- before the edit --\n").Append(cmp());
// a real edit through the window's own control: the Swarm section header toggle
var secT = ZType("ZuiSection"); UnityEngine.UIElements.Toggle sw=null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!secT.IsInstanceOfType(e)||!ZDrawn(e)) continue;
  bool isS=false; foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null&&l.ClassListContains("zui-section__title")&&l.text=="Swarm"){isS=true;break;} }
  if (!isS) continue; foreach (var c in ZAll(e)) if (c is UnityEngine.UIElements.Toggle tg && c.ClassListContains("zui-section__toggle")) { sw=tg; break; } break; }
if (sw==null) return sb.Append("no swarm toggle").ToString();
sw.value = !sw.value;
sb.Append("-- after toggling Swarm to ").Append(sw.value).Append(" --\n").Append(cmp());
return sb.ToString();
