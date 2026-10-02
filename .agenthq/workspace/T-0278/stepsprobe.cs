// eval_file: Rect primitive, Height stage with technique=Stepped and bevel=Stepped, depth>0.
// Test steps and bevelSteps to see if they change rendered pixels.
var doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = 96; doc.canvasHeight = 96; doc.frameCount = 16;
doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var layer = new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, root = new Laubrary.Shaper.ShaperNode { name = "Shape" } };
layer.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
layer.root.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect };
layer.root.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill();
layer.response.normalKind = Laubrary.Shaper.ShaperNormalKind.Profile;
layer.height = new Laubrary.Shaper.ShaperHeightDef();
layer.height.depth.staticValue = 24f;
layer.height.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped;
layer.height.bevel = Laubrary.Shaper.ShaperBevelTechnique.Stepped;
layer.height.bevelAmount.staticValue = 0.4f;
doc.layers.Add(layer);
System.Func<UnityEngine.Color32[]> render = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 8);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };
var sb = new System.Text.StringBuilder();
var baseline = render();
int opaque = 0; foreach (var p in baseline) if (p.a > 0) opaque++;
sb.Append("baseline opaque=").Append(opaque).Append('/').Append(baseline.Length).Append('\n');

var h = layer.height;
System.Action<string, System.Action, System.Action> test = (name, apply, revert) => { apply(); var r = render(); sb.Append(name).Append(": ").Append(diff(baseline, r)).Append(" px changed\n"); revert(); };

// sanity: does depth itself register?
test("depth 24->60", () => h.depth.staticValue = 60f, () => h.depth.staticValue = 24f);
test("technique Stepped->Dome", () => h.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, () => h.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped);
test("bevel Stepped->None", () => h.bevel = Laubrary.Shaper.ShaperBevelTechnique.None, () => h.bevel = Laubrary.Shaper.ShaperBevelTechnique.Stepped);
test("steps 4->32 (technique=Stepped)", () => h.steps.staticValue = 32f, () => h.steps.staticValue = 4f);
test("steps 4->2 (technique=Stepped)", () => h.steps.staticValue = 2f, () => h.steps.staticValue = 4f);
test("bevelSteps 3->16 (bevel=Stepped)", () => h.bevelSteps.staticValue = 16f, () => h.bevelSteps.staticValue = 3f);
test("bevelSteps 3->2 (bevel=Stepped)", () => h.bevelSteps.staticValue = 2f, () => h.bevelSteps.staticValue = 3f);

// Directly check the compiled ShaperHeightOp's n/bevelN via reflection-free path: use ShaperHeightCompiler.Compile directly.
var prog = Laubrary.Shaper.ShaperCompiler.Compile(layer.root, doc.phase01, doc.seed);
float baseZ = Laubrary.Shaper.ShaperHeightCompiler.LayerBase(doc, 0, doc.phase01, doc.seed);
var op4 = Laubrary.Shaper.ShaperHeightCompiler.Compile(h, prog, doc.pixelSize, baseZ, doc.phase01, doc.seed);
sb.Append("compiled op with steps=4: n=").Append(op4.n).Append(" bevelN=").Append(op4.bevelN).Append(" body=").Append(op4.body).Append(" present=").Append(op4.present).Append('\n');
h.steps.staticValue = 32f;
var op32 = Laubrary.Shaper.ShaperHeightCompiler.Compile(h, prog, doc.pixelSize, baseZ, doc.phase01, doc.seed);
sb.Append("compiled op with steps=32: n=").Append(op32.n).Append(" bevelN=").Append(op32.bevelN).Append('\n');
h.steps.staticValue = 4f;

UnityEngine.Object.DestroyImmediate(doc);
return sb.ToString();
