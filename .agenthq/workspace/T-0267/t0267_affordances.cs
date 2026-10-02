// T-0267 — sweep every Add/enable affordance + every Shape-picker entry on a fresh in-memory document,
// measuring RenderFrame diff at 3 frames (0, mid, last) against a pre-affordance baseline.
var sb = new System.Text.StringBuilder();

var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });

// Mirrors ShaperWindow.InitializeNewAsset exactly (ShaperWindow.cs:86-99): the REAL first-run document a
// fresh "Laubrary/Shaper" new-document click produces, not a hand-built stand-in — so a flaw found here is
// a flaw an author actually hits.
System.Func<Laubrary.Shaper.ShaperDocument> fresh = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.layers.Add(NewLayer("Layer 1", d));
    d.lightRig ??= new Laubrary.Shaper.ShaperLightRig();
    d.lightRig.lights ??= new System.Collections.Generic.List<Laubrary.Shaper.ShaperLight>();
    if (d.lightRig.lights.Count == 0) d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    return d;
};

System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render = (d, f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };
System.Func<UnityEngine.Color32[], int> opaque = (a) => { int n=0; foreach(var p in a) if (p.a>0) n++; return n; };

System.Action<string, System.Action<Laubrary.Shaper.ShaperDocument>> test = (name, apply) =>
{
    var d = fresh();
    var frames = new[] { 0, 7, 15 };
    var before = new UnityEngine.Color32[frames.Length][];
    for (int i=0;i<frames.Length;i++) before[i] = render(d, frames[i]);
    int beforeOpaque = 0; foreach(var b in before) beforeOpaque += opaque(b);
    try { apply(d); }
    catch (System.Exception e) { sb.Append(name).Append(": THREW ").Append(e.Message).Append('\n'); UnityEngine.Object.DestroyImmediate(d); return; }
    var after = new UnityEngine.Color32[frames.Length][];
    for (int i=0;i<frames.Length;i++) after[i] = render(d, frames[i]);
    int afterOpaque = 0; foreach(var a in after) afterOpaque += opaque(a);
    int totalDiff = 0; for (int i = 0; i < frames.Length; i++) totalDiff += diff(before[i], after[i]);
    sb.Append(name).Append(": diffPx=").Append(totalDiff).Append(" opaqueBefore=").Append(beforeOpaque)
      .Append(" opaqueAfter=").Append(afterOpaque).Append('\n');
    UnityEngine.Object.DestroyImmediate(d);
};

// Bag member is created fill-LESS by the window's own "+ Add member" lambda (ShaperWindow.Sections.cs:1740-1747:
// `new ShaperNode { name = "Member " + ... }`, no fill set) — the genuine "no fill yet" case Add-fill targets.
test("Add member (bag), member has no fill/kind yet", d => { d.layers[0].root.kind = Laubrary.Shaper.ShaperNodeKind.Bag; d.layers[0].root.children = new System.Collections.Generic.List<Laubrary.Shaper.ShaperNode> { new Laubrary.Shaper.ShaperNode { name = "Member 1" } }; });
test("Add fill (on that bag member, node.fill = new ShaperFillDef{authored=true})", d => {
    d.layers[0].root.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    var member = new Laubrary.Shaper.ShaperNode { name = "Member 1" };
    d.layers[0].root.children = new System.Collections.Generic.List<Laubrary.Shaper.ShaperNode> { member };
    member.fill = new Laubrary.Shaper.ShaperFillDef { authored = true };
});
test("Add edge (on layer root, which already has a shape/fill)", d => { d.layers[0].root.border = new Laubrary.Shaper.ShaperBorderDef { authored = true, enabled = true, fill = new Laubrary.Shaper.ShaperFillDef { authored = true } }; });
test("Add edge fill (edge exists, fill empty)", d => { d.layers[0].root.border = new Laubrary.Shaper.ShaperBorderDef { authored = true, enabled = true }; d.layers[0].root.border.fill = new Laubrary.Shaper.ShaperFillDef { authored = true }; });
test("Add light (2nd light, default)", d => { d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Light 2" }); });
test("Height on (NewHeightStage: Dome depth 8)", d => { d.layers[0].height = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, depth = new ZUIValue(8f) }; });
test("Mask on (2nd layer as source, Coverage/Clip)", d => { var l2 = new Laubrary.Shaper.ShaperLayer { name = "M", enabled = true, root = new Laubrary.Shaper.ShaperNode { name = "S2", kind = Laubrary.Shaper.ShaperNodeKind.Primitive, fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill() } }; l2.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; d.layers.Add(l2); d.layers[0].mask = new Laubrary.Shaper.ShaperLayerMask(); d.layers[0].mask.sourceLayerId = d.IdOf(l2); });
test("Swarm on, DEFAULT (count 5, jitter 24)", d => { d.layers[0].root.swarm.enabled = true; });
test("Sweep on, DEFAULT (extent 360 = identity)", d => { d.layers[0].root.sweep.enabled = true; });
test("Shell on, DEFAULT (thickness 4)", d => { d.layers[0].root.shell.enabled = true; });
test("Lighting off->on (already true by default)", d => { d.layers[0].response.receiveLighting = false; d.layers[0].response.receiveLighting = true; });
test("Cherry on, no slots yet", d => { d.cherryEnabled = true; });

sb.Append("---SHAPE PICKER CATALOG---\n");
foreach (var entry in Laubrary.Shaper.Editor.ShaperShapeCatalog.All())
{
    var e = entry;
    string label = e.Category + "/" + e.Label;
    test("Pick: " + label, d => { e.Apply(d.layers[0].root); if (d.layers[0].root.fill == null) d.layers[0].root.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill(); });
}

return sb.ToString();
