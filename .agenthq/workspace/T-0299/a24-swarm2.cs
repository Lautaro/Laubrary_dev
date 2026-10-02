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
var sw = node.swarm;
var swT = sw.GetType();

System.Func<UnityEngine.Color32[]> Px = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 4);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

System.Func<string, int> Sweep = name =>
{
    var f = swT.GetField(name);
    object orig = f.GetValue(sw);
    object test = null;
    if (f.FieldType == typeof(ZUIValue)) test = new ZUIValue(((ZUIValue)orig).staticValue + 17f);
    else if (f.FieldType == typeof(float)) test = UnityEngine.Mathf.Approximately((float)orig, 0.5f) ? 0.9f : 0.5f;
    else if (f.FieldType == typeof(bool)) test = !((bool)orig);
    else if (f.FieldType.IsEnum) { foreach (var v in System.Enum.GetValues(f.FieldType)) if (!v.Equals(orig)) { test = v; break; } }
    var b0 = Px();
    changeM.Invoke(win, new object[] { (System.Action)(() => f.SetValue(sw, test)) });
    int d = Diff(b0, Px());
    changeM.Invoke(win, new object[] { (System.Action)(() => f.SetValue(sw, orig)) });
    return d;
};

// STATE A: a spawner SHAPE is picked (spawner* dials should come alive)
changeM.Invoke(win, new object[] { (System.Action)(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.Circle; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area; }) });
sb.Append("STATE A  shape=Circle, spawnMode=Area\n");
foreach (var n in new string[] { "spawnerRadius", "spawnerOffsetX", "spawnerOffsetY", "spawnerRotationDegrees", "spawnerPitchDegrees", "spawnerYawDegrees", "distribution", "spawnOrderChaos", "orient", "scaleByIndex", "rotationJitterDegreesDial" })
    sb.Append(Sweep(n) == 0 ? "  DEAD " : "  live ").Append(n).Append(" changed=").Append(Sweep(n)).Append("\n");

// STATE B: spawnMode = Path (path* dials should come alive)
changeM.Invoke(win, new object[] { (System.Action)(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.Circle; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Path; }) });
sb.Append("STATE B  shape=Circle, spawnMode=Path\n");
foreach (var n in new string[] { "pathProgress", "evenSpacing", "pathSpread", "gridReverse", "distribution" })
    sb.Append(Sweep(n) == 0 ? "  DEAD " : "  live ").Append(n).Append(" changed=").Append(Sweep(n)).Append("\n");

// STATE C: timing = Window / Lifetime dials
changeM.Invoke(win, new object[] { (System.Action)(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.Circle; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area; sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Window; }) });
sb.Append("STATE C  timing=Window\n");
foreach (var n in new string[] { "spawnTiming", "firstSpawnPhase", "spawnPhaseStep", "instanceLife", "dieTogether", "lifetimeStagger" })
    sb.Append(Sweep(n) == 0 ? "  DEAD " : "  live ").Append(n).Append(" changed=").Append(Sweep(n)).Append("\n");

changeM.Invoke(win, new object[] { (System.Action)(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.None; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area; sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger; }) });
return sb.ToString();
