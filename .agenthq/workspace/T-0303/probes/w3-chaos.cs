var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var s = doc.layers[0].root.swarm;
s.enabled = true; s.count = 8; s.EnsureDials();
System.Func<int, int[]> H = f => { var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f); var o = new int[px.Length]; for (int i=0;i<px.Length;i++) o[i]=(px[i].r<<24)|(px[i].g<<16)|(px[i].b<<8)|px[i].a; return o; };
System.Func<int[],int[],int> D = (a,b) => { int d=0; for (int i=0;i<a.Length;i++) if (a[i]!=b[i]) d++; return d; };
System.Func<string> M = () => { int tot=0; for (int f=0;f<doc.frameCount;f+=4){ var b=H(f); s.spawnOrderChaos=1f; var a=H(f); s.spawnOrderChaos=0f; tot+=D(b,a);} return tot.ToString(); };
s.spawnerRadius.staticValue = 24f;
foreach (var sh in new[]{ Laubrary.Shaper.ShaperSwarmShape.None, Laubrary.Shaper.ShaperSwarmShape.Circle })
  foreach (var ti in new[]{ Laubrary.Shaper.ShaperSwarmTiming.Stagger, Laubrary.Shaper.ShaperSwarmTiming.Window, Laubrary.Shaper.ShaperSwarmTiming.FrameStep })
  { s.shape = sh; s.timing = ti; sb.Append("shape=").Append(sh).Append(" timing=").Append(ti).Append("  Appearance order 0->1 moves ").Append(M()).Append(" px   (UI draws it: ").Append(ti == Laubrary.Shaper.ShaperSwarmTiming.Stagger ? "NO" : "yes").Append(")\n"); }
s.shape = Laubrary.Shaper.ShaperSwarmShape.None; s.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger; s.spawnOrderChaos = 0f; s.enabled = false; s.count = 5;
return sb.ToString();
