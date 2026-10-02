// A12 — Text primitive: Line spacing and Align measured dead at the default single-line string.
// Do they act with a real multi-line string? And what does the card draw for them?
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BFs);

var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 8; d.seed = 7u;
d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Text", d });
l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Text;
d.layers.Add(l);
var p = l.root.primitive;
SB.Append("default textString = \"").Append(p.textString).Append("\"  lineSpacing=")
  .Append(p.textLineSpacingDial.staticValue).Append(" align=").Append(p.textAlign).Append('\n');

System.Func<int, UnityEngine.Color32[]> R = fi => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, fi);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++; return n; };

foreach (var s in new string[] { p.textString, "AB\nCD", "AB\nCD\nEF" })
{
    p.textString = s;
    p.textLineSpacingDial = new ZUIValue(0f);
    p.textAlign = Laubrary.Shaper.ShaperTextAlign.Centre;
    var b0 = R(0);
    int lit = 0; foreach (var c in b0) if (c.a > 0) lit++;

    p.textLineSpacingDial = new ZUIValue(16f);
    int dLine = Diff(b0, R(0));
    p.textLineSpacingDial = new ZUIValue(-6f);
    int dLine2 = Diff(b0, R(0));
    p.textLineSpacingDial = new ZUIValue(0f);

    var sb2 = new System.Text.StringBuilder();
    foreach (var al in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperTextAlign)))
    {
        p.textAlign = (Laubrary.Shaper.ShaperTextAlign)al;
        sb2.Append(al).Append('=').Append(Diff(b0, R(0))).Append(' ');
    }
    p.textAlign = Laubrary.Shaper.ShaperTextAlign.Centre;

    SB.Append("string=\"").Append(s.Replace("\n", "\\n")).Append("\" lit=").Append(lit)
      .Append("  lineSpacing 0->16=").Append(dLine).Append(" 0->-6=").Append(dLine2)
      .Append("  align: ").Append(sb2).Append('\n');
}
UnityEngine.Object.DestroyImmediate(d);
return SB.ToString();
