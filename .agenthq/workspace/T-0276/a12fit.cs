// A12 — is Fit really inert on a Colour-bands fill and a Height-field fill, or was the fixture wrong?
// Read the compiled op's own anchor divisors as well as the picture, so the answer is not a guess.
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BFs);

var hf = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RFloat, false);
var hp = new UnityEngine.Color[32 * 32];
for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
    hp[y * 32 + x] = new UnityEngine.Color(UnityEngine.Mathf.Abs(x - 16) / 16f, 0, 0, 1);
hf.SetPixels(hp); hf.Apply();

var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 4; d.seed = 7u;
d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "N", d });
l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
l.root.primitive.ellipseRxDial = new ZUIValue(34f);
l.root.primitive.ellipseRyDial = new ZUIValue(13f);
d.layers.Add(l);
var f = l.root.fill;
f.heightField = hf;

System.Func<UnityEngine.Color32[]> R = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, 0);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++; return n; };

SB.Append("kind\tspace\trepeats\tslots\tfitPx\n");
foreach (var k in new Laubrary.Shaper.ShaperFillKind[] {
    Laubrary.Shaper.ShaperFillKind.IndexedStrip, Laubrary.Shaper.ShaperFillKind.HeightField })
{
    f.kind = k;
    foreach (var sp in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperFillSpace)))
    {
        f.space = (Laubrary.Shaper.ShaperFillSpace)sp;
        foreach (var rep in new float[] { 1f, 4f })
        {
            f.stripRepeats = new ZUIValue(rep);
            f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
            var b0 = R();
            f.fit = Laubrary.Shaper.ShaperFillFit.Stretch;
            int px = Diff(b0, R());
            f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
            SB.Append(k).Append('\t').Append(sp).Append('\t').Append(rep).Append('\t')
              .Append(f.stripSlots == null ? -1 : f.stripSlots.Count).Append('\t').Append(px).Append('\n');
        }
    }
}
// and a Gradient/Linear at a non-zero angle, which is the state Fit could plausibly reach
f.kind = Laubrary.Shaper.ShaperFillKind.Gradient;
f.space = Laubrary.Shaper.ShaperFillSpace.Stamped;
f.gradientMode = Laubrary.Shaper.ShaperGradientMode.Linear;
foreach (var ang in new float[] { 0f, 30f, 45f, 90f })
{
    f.gradientAngleDegrees = new ZUIValue(ang);
    f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
    var b0 = R();
    f.fit = Laubrary.Shaper.ShaperFillFit.Stretch;
    SB.Append("Gradient/Linear angle ").Append(ang).Append('\t').Append(Diff(b0, R())).Append('\n');
    f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
}
UnityEngine.Object.DestroyImmediate(d);
UnityEngine.Object.DestroyImmediate(hf);
return SB.ToString();
