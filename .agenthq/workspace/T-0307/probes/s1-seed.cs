// What does the Canvas card's "Document seed" actually reach? The tooltip names two consumers
// ("Min-Max light dials and cherry-frame picks"). Measured against the engine on in-memory documents.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render = (d, f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };
System.Func<Laubrary.Shaper.ShaperDocument, int> lit = d => { var px = render(d, 0); int n = 0; foreach (var p in px) if (p.a > 0) n++; return n; };

System.Func<Laubrary.Shaper.ShaperDocument> fresh = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.layers.Add(NewLayer("Layer 1", d));
    return d;
};

System.Func<Laubrary.Shaper.ShaperDocument, int> seedSweep = d =>
{
    int moved = 0;
    foreach (int f in new[] { 0, 4, 8, 12 })
    {
        d.seed = 1u; var a = render(d, f);
        d.seed = 777u; var b = render(d, f);
        moved += diff(a, b);
    }
    d.seed = 1u;
    return moved;
};

// 1) a brand-new document, nothing but the seeded default layer, NO lights added and no cherry frames
var d1 = fresh();
sb.Append("plain new document (lit frame0=").Append(lit(d1)).Append("): seed 1 -> 777 moves ").Append(seedSweep(d1)).Append(" px\n");

// 2) the same document with a swarm placed on its root node
var d2 = fresh();
var root2 = d2.layers[0].root;
root2.swarm = new Laubrary.Shaper.ShaperSwarmDef { enabled = true, shape = Laubrary.Shaper.ShaperSwarmShape.Circle };
root2.swarm.count = 8;
root2.swarm.spawnerRadius = new ZUIValue(24f);
sb.Append("with a Circle swarm of 8: seed 1 -> 777 moves ").Append(seedSweep(d2)).Append(" px\n");

// 3) the same document with one Min-Max positional jitter on the swarm (a Min-Max dial, the tooltip's case)
var d3 = fresh();
var root3 = d3.layers[0].root;
root3.swarm = new Laubrary.Shaper.ShaperSwarmDef { enabled = true, shape = Laubrary.Shaper.ShaperSwarmShape.Circle };
root3.swarm.count = 8;
root3.swarm.spawnerRadius = new ZUIValue(24f);
root3.swarm.positionJitterX = new ZUIValue(6f); root3.swarm.positionJitterY = new ZUIValue(6f);
sb.Append("swarm + position jitter 6: seed 1 -> 777 moves ").Append(seedSweep(d3)).Append(" px\n");

// 4) with a light in the rig, no swarm (the tooltip's other named case)
var d4 = fresh();
d4.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
sb.Append("one plain light, no swarm: seed 1 -> 777 moves ").Append(seedSweep(d4)).Append(" px\n");

UnityEngine.Object.DestroyImmediate(d1); UnityEngine.Object.DestroyImmediate(d2);
UnityEngine.Object.DestroyImmediate(d3); UnityEngine.Object.DestroyImmediate(d4);
return sb.ToString();
