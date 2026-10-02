// Does a LIGHT's own Min-Max dial follow the document seed? Reads the compiled rig, not pixels.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<float, float, ZUIValue> MM = (lo, hi) => { var v = new ZUIValue(lo); v.mode = ZUIValue.Mode.MinMax; v.min = lo; v.max = hi; return v; };

var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d.layers.Add(NewLayer("Layer 1", d));
var light = new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true };
sb.Append("ShaperLight.intensity type=").Append(light.intensity == null ? "<null>" : light.intensity.GetType().Name).Append("\n");
light.intensity = MM(0.2f, 3f);
light.kind = Laubrary.Shaper.ShaperLightKind.Point;
d.lightRig.lights.Add(light);
d.layers[0].response.receiveLighting = true;

foreach (uint s in new uint[] { 1u, 777u, 999u })
{
    d.seed = s;
    float raw = Laubrary.Shaper.ShaperValue.Sample(light.intensity, 0f, s + 16u + 0u, 1f);
    var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(d.lightRig, 0f, s);
    sb.Append("seed=").Append(s).Append("  ShaperValue.Sample(intensity)=").Append(raw.ToString("F4"));
    // read whatever the compiled program exposes for light 0
    var t = prog.GetType();
    var cnt = t.GetField("count", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    sb.Append("  progType=").Append(t.Name).Append(" count=").Append(cnt == null ? "?" : cnt.GetValue(prog).ToString());
    sb.Append("\n");
}
// and the ambient, which the same Compile samples at seed+1
var amb = MM(0f, 2f);
d.lightRig.ambientIntensity = amb;
foreach (uint s in new uint[] { 1u, 777u })
    sb.Append("ambient Min-Max at seed ").Append(s).Append(" = ")
      .Append(Laubrary.Shaper.ShaperValue.Sample(amb, 0f, s + 1u, 0.18f).ToString("F4")).Append("\n");

UnityEngine.Object.DestroyImmediate(d);
return sb.ToString();
