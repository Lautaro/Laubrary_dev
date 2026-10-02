// A19 — every Solid form's Position box: what the engine actually does with Scale Y / Skew Y / Rotation,
// against what the box declares (ShaperSolids.InertReason) — measured, not asserted.
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BFs);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++; return n; };

SB.Append("form\tlit\tscaleX\tscaleY\tskewX\tskewY\trotation\ttranslate\torigin\tdeclScaleY\tdeclRoll\n");
foreach (Laubrary.Shaper.ShaperSolidForm form in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperSolidForm)))
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 4; d.seed = 7u;
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "S", d });
    l.root.kind = Laubrary.Shaper.ShaperNodeKind.Solid;
    if (l.root.solid == null) l.root.solid = new Laubrary.Shaper.ShaperSolidDef();
    l.root.solid.form = form;
    d.layers.Add(l);
    var t = l.root.transform; t.EnsureDials();
    System.Func<UnityEngine.Color32[]> R = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, 0);
    var b0 = R();
    int lit = 0; foreach (var c in b0) if (c.a > 0) lit++;
    System.Func<ZUIValue, float, int> Move = (z, v) =>
    { float keep = z.staticValue; var km = z.mode; z.mode = ZUIValue.Mode.Static; z.staticValue = v; int n = Diff(b0, R()); z.staticValue = keep; z.mode = km; return n; };
    int sx = Move(t.scaleX, 1.8f), sy = Move(t.scaleY, 1.8f);
    int kx = Move(t.skewX, 30f), ky = Move(t.skewY, 30f);
    int rot = Move(t.rotationDegrees, 40f);
    int tr = Move(t.translateX, 12f), og = Move(t.originX, 12f);
    string declY = Laubrary.Shaper.ShaperSolids.InertReason(form, Laubrary.Shaper.ShaperSolidDial.Aspect);
    string declR = Laubrary.Shaper.ShaperSolids.InertReason(form, Laubrary.Shaper.ShaperSolidDial.Roll);
    SB.Append(form).Append('\t').Append(lit).Append('\t').Append(sx).Append('\t').Append(sy).Append('\t')
      .Append(kx).Append('\t').Append(ky).Append('\t').Append(rot).Append('\t').Append(tr).Append('\t').Append(og)
      .Append('\t').Append(declY == null ? "live" : "INERT").Append('\t').Append(declR == null ? "live" : "INERT").Append('\n');
    UnityEngine.Object.DestroyImmediate(d);
}
return SB.ToString();
