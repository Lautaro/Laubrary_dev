// Does any swarm dial that the UI HIDES in the current state still move pixels in that state?
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var node = doc.layers[0].root;
var s = node.swarm;
s.enabled = true; s.count = 8; s.EnsureDials();

System.Func<int, int[]> Hash = f => {
    var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f);
    var o = new int[px.Length];
    for (int i = 0; i < px.Length; i++) o[i] = (px[i].r << 24) | (px[i].g << 16) | (px[i].b << 8) | px[i].a;
    return o; };
System.Func<int[], int[], int> Diff = (a, b) => { int d = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++; return d; };
System.Func<System.Action, System.Action, string> Move = (set, unset) => {
    int tot = 0;
    for (int f = 0; f < doc.frameCount; f += 4) { var before = Hash(f); set(); var after = Hash(f); unset(); tot += Diff(before, after); }
    return tot.ToString(); };

// STATE: shape=Circle, spawn=Area, timing=Stagger  (the state where the UI hides Lifetime / Die together / Appearance order)
s.shape = Laubrary.Shaper.ShaperSwarmShape.Circle;
s.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area;
s.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger;
s.spawnerRadius.staticValue = 24f;
sb.Append("STATE: shape=Circle spawn=Area timing=Stagger (UI draws: Distribution, Face, Size by index, Lifetime stagger)\n");
{
    float k = s.spawnOrderChaos; sb.Append("  spawnOrderChaos (HIDDEN here) 0->1 moves ")
      .Append(Move(() => s.spawnOrderChaos = 1f, () => s.spawnOrderChaos = k)).Append(" px\n");
    float l = s.instanceLife; sb.Append("  instanceLife (HIDDEN here) ").Append(l).Append("->0.25 moves ")
      .Append(Move(() => s.instanceLife = 0.25f, () => s.instanceLife = l)).Append(" px\n");
    bool d = s.dieTogether; sb.Append("  dieTogether (HIDDEN here) ").Append(d).Append("->").Append(!d).Append(" moves ")
      .Append(Move(() => s.dieTogether = !d, () => s.dieTogether = d)).Append(" px\n");
    float fs = s.firstSpawnPhase; sb.Append("  firstSpawnPhase (HIDDEN here) ->0.5 moves ")
      .Append(Move(() => s.firstSpawnPhase = 0.5f, () => s.firstSpawnPhase = fs)).Append(" px\n");
    float ps = s.pathSpread; sb.Append("  pathSpread (HIDDEN here, Area) ->0.4 moves ")
      .Append(Move(() => s.pathSpread = 0.4f, () => s.pathSpread = ps)).Append(" px\n");
    float pp = s.pathProgress.staticValue; sb.Append("  pathProgress (HIDDEN here, Area) ->0.5 moves ")
      .Append(Move(() => s.pathProgress.staticValue = 0.5f, () => s.pathProgress.staticValue = pp)).Append(" px\n");
    bool gr = s.gridReverse; sb.Append("  gridReverse ('Fill from the edge', DRAWN here) flip moves ")
      .Append(Move(() => s.gridReverse = !gr, () => s.gridReverse = gr)).Append(" px\n");
    float ls = s.lifetimeStagger; sb.Append("  lifetimeStagger (DRAWN here) ->0.9 moves ")
      .Append(Move(() => s.lifetimeStagger = 0.9f, () => s.lifetimeStagger = ls)).Append(" px\n");
}

// STATE: shape=None, timing=Stagger  (the UI shows only Count/seed/jitters/Shape/Timing/Lifetime stagger)
s.shape = Laubrary.Shaper.ShaperSwarmShape.None;
sb.Append("STATE: shape=None timing=Stagger\n");
{
    float r = s.spawnerRadius.staticValue; sb.Append("  spawnerRadius (HIDDEN here) ->48 moves ")
      .Append(Move(() => s.spawnerRadius.staticValue = 48f, () => s.spawnerRadius.staticValue = r)).Append(" px\n");
    float dd = s.distribution; sb.Append("  distribution (HIDDEN here) ->1 moves ")
      .Append(Move(() => s.distribution = 1f, () => s.distribution = dd)).Append(" px\n");
    float sx = s.spawnerOffsetX.staticValue; sb.Append("  spawnerOffsetX (HIDDEN here) ->20 moves ")
      .Append(Move(() => s.spawnerOffsetX.staticValue = 20f, () => s.spawnerOffsetX.staticValue = sx)).Append(" px\n");
    var or = s.orient; sb.Append("  orient (HIDDEN here) ->AwayFromCentre moves ")
      .Append(Move(() => s.orient = Laubrary.Shaper.ShaperSwarmOrient.Outward, () => s.orient = or)).Append(" px\n");
    float sbi = s.scaleByIndex.staticValue; sb.Append("  scaleByIndex (HIDDEN here) ->0.4 moves ")
      .Append(Move(() => s.scaleByIndex.staticValue = 0.4f, () => s.scaleByIndex.staticValue = sbi)).Append(" px\n");
}

// restore
s.shape = Laubrary.Shaper.ShaperSwarmShape.None;
s.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area;
s.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger;
s.enabled = false; s.count = 5;
return sb.ToString();
