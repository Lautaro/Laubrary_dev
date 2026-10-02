// eval_file: T-0278 — measure Steps/Bevel steps on SAVED Rect and Star documents, reloaded from disk, 3 frames each.
string dir = "Assets/Shaper";
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");

System.Func<Laubrary.Shaper.ShaperPrimitiveKind, string, string> build = (kind, path) =>
{
    var doc = ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    doc.canvasWidth = 96; doc.canvasHeight = 96; doc.frameCount = 16;
    doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    var layer = new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, root = new Laubrary.Shaper.ShaperNode { name = "Shape" } };
    layer.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
    layer.root.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = kind };
    layer.root.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill();
    layer.response.normalKind = Laubrary.Shaper.ShaperNormalKind.Profile;
    layer.height = new Laubrary.Shaper.ShaperHeightDef();
    layer.height.depth.staticValue = 24f;
    layer.height.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped;
    layer.height.bevel = Laubrary.Shaper.ShaperBevelTechnique.Stepped;
    layer.height.bevelAmount.staticValue = 0.4f;
    doc.layers.Add(layer);
    UnityEditor.AssetDatabase.CreateAsset(doc, path);
    UnityEditor.AssetDatabase.SaveAssets();
    return path;
};

string rectPath = build(Laubrary.Shaper.ShaperPrimitiveKind.Rect, dir + "/AuditT0278Rect.asset");
string starPath = build(Laubrary.Shaper.ShaperPrimitiveKind.Star, dir + "/AuditT0278Star.asset");
UnityEditor.AssetDatabase.Refresh();

System.Func<string, string> measure = (path) =>
{
    var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
    var sb = new System.Text.StringBuilder();
    sb.Append("== ").Append(path).Append(" (reloaded, present=").Append(doc.layers[0].height != null).Append(") ==\n");
    int[] frames = { 0, doc.frameCount / 2, doc.frameCount - 1 };
    var h = doc.layers[0].height;
    foreach (int f in frames)
    {
        var before = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f);
        int opaqueBefore = 0; foreach (var p in before) if (p.a > 0) opaqueBefore++;

        h.steps.staticValue = 32f;
        var afterSteps = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f);
        int diffSteps = 0; for (int i = 0; i < before.Length; i++) if (!before[i].Equals(afterSteps[i])) diffSteps++;
        h.steps.staticValue = 4f;

        h.bevelSteps.staticValue = 16f;
        var afterBevel = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f);
        int diffBevel = 0; for (int i = 0; i < before.Length; i++) if (!before[i].Equals(afterBevel[i])) diffBevel++;
        h.bevelSteps.staticValue = 3f;

        // sanity: technique change SHOULD move pixels (proves height stage is live)
        h.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome;
        var afterTech = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f);
        int diffTech = 0; for (int i = 0; i < before.Length; i++) if (!before[i].Equals(afterTech[i])) diffTech++;
        h.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped;

        sb.Append("frame ").Append(f).Append(": opaque=").Append(opaqueBefore).Append('/').Append(before.Length)
          .Append(" steps4->32 diff=").Append(diffSteps)
          .Append(" bevelSteps3->16 diff=").Append(diffBevel)
          .Append(" technique Stepped->Dome diff=").Append(diffTech).Append('\n');
    }
    return sb.ToString();
};

string result = measure(rectPath) + measure(starPath);

UnityEditor.AssetDatabase.DeleteAsset(rectPath);
UnityEditor.AssetDatabase.DeleteAsset(starPath);
UnityEditor.AssetDatabase.Refresh();

return result;
