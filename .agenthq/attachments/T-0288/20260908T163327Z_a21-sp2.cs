var sb = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<Laubrary.Shaper.ShaperDocument> fresh = () => {
  var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
  d.canvasWidth=96; d.canvasHeight=64; d.frameCount=16;
  d.layers.Add((Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[]{"Layer 1", d}));
  d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name="Key", enabled=true });
  return d; };
foreach (var c in Laubrary.Shaper.Editor.ShaperShapeCatalog.Columns()) {
  if (c.Key != "Pyre") continue;
  foreach (var e in c.Value) {
    var d = fresh();
    e.Apply(d.layers[0].root);
    int best=0; string frames="";
    foreach (int f in new[]{0,4,8,12}) {
      var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
      int lit=0; foreach (var p in px) if (p.a>0) lit++;
      frames += lit + " "; if (lit>best) best=lit; }
    sb.Append(c.Key).Append('\t').Append(e.Label).Append('\t').Append(frames).Append('\t').Append(best>0?"DRAWS":"BLANK").Append('\t').Append("tipLen=").Append(e.Tooltip==null?0:e.Tooltip.Length).Append('\n');
    UnityEngine.Object.DestroyImmediate(d);
  }
}
return sb.ToString();
