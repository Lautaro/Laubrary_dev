var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var changeM = WT.GetMethod("Change", BFi);
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var node = doc.layers[0].root;

// a STAR (not rotationally symmetric) so a rotation dial can possibly show
changeM.Invoke(win, new object[] { (System.Action)(() => {
    node.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star };
    node.swarm.enabled = true; node.swarm.count = 5;
    node.swarm.shape = Laubrary.Shaper.ShaperSwarmShape.Circle;
    node.swarm.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area;
    node.swarm.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger;
}) });
var sw = node.swarm; var swT = sw.GetType();

// MAX changed pixels across the WHOLE loop, not one frame - a timing dial only shows on some frames
System.Func<string, int> SweepAllFrames = name =>
{
    var f = swT.GetField(name);
    object orig = f.GetValue(sw);
    object test = null;
    if (f.FieldType == typeof(ZUIValue)) test = new ZUIValue(((ZUIValue)orig).staticValue + 37f);
    else if (f.FieldType == typeof(float)) test = UnityEngine.Mathf.Approximately((float)orig, 0.5f) ? 0.9f : 0.5f;
    else if (f.FieldType == typeof(bool)) test = !((bool)orig);
    else if (f.FieldType.IsEnum) { foreach (var v in System.Enum.GetValues(f.FieldType)) if (!v.Equals(orig)) { test = v; break; } }
    var before = new UnityEngine.Color32[doc.frameCount][];
    for (int i = 0; i < doc.frameCount; i++) before[i] = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, i);
    changeM.Invoke(win, new object[] { (System.Action)(() => f.SetValue(sw, test)) });
    int max = 0;
    for (int i = 0; i < doc.frameCount; i++)
    {
        var now = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, i);
        int n = 0; for (int k = 0; k < now.Length; k++) if (before[i][k].r != now[k].r || before[i][k].g != now[k].g || before[i][k].b != now[k].b || before[i][k].a != now[k].a) n++;
        if (n > max) max = n;
    }
    changeM.Invoke(win, new object[] { (System.Action)(() => f.SetValue(sw, orig)) });
    return max;
};

sb.Append("STAR primitive, swarm Circle/Area/Stagger - max changed px over all 16 frames\n");
foreach (var n in new string[] { "orient", "rotationJitterDegreesDial", "scaleByIndex", "lifetimeStagger" })
    sb.Append("  ").Append(n).Append(" = ").Append(SweepAllFrames(n)).Append("\n");

// the lifetime family under timing = Lifetime
foreach (var tv in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperSwarmTiming)))
{
    changeM.Invoke(win, new object[] { (System.Action)(() => sw.timing = (Laubrary.Shaper.ShaperSwarmTiming)tv) });
    sb.Append("timing=").Append(tv).Append(":");
    foreach (var n in new string[] { "spawnTiming", "firstSpawnPhase", "spawnPhaseStep", "instanceLife", "dieTogether", "lifetimeStagger" })
        sb.Append(" ").Append(n).Append("=").Append(SweepAllFrames(n));
    sb.Append("\n");
}
changeM.Invoke(win, new object[] { (System.Action)(() => sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger) });
return sb.ToString();
