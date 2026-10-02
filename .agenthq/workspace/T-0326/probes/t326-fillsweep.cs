// T-0277 - T-0265's fill rows, re-run. Same method (build the document, write it to disk, re-import with
// ForceUpdate, re-load, then perturb every control of the Fill card and the Edge card's own fill in the
// state that card shows it in) and the same candidate policy, restricted to the three node kinds whose
// card actually shows a Fill: a Primitive, a Solid and a Bag. Fill is ABSENT on a Composite (T-0265).
var PUB = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var sb = new System.Text.StringBuilder();
string dir = "Assets/Shaper/Audit0277";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "Audit0277");

// the same 16x16 checkerboard T-0265 used, and a REAL RFloat height field (T-0265 handed the height-field
// fill an RGBA texture, which GetPixelData<float> cannot read - see the handover).
var tex = new UnityEngine.Texture2D(16, 16, UnityEngine.TextureFormat.RGBA32, false);
for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
    tex.SetPixel(x, y, ((x / 4 + y / 4) % 2 == 0) ? UnityEngine.Color.red : UnityEngine.Color.blue);
tex.Apply();
var hf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(dir + "/rfield0277.asset");

System.Action<ZuiGradient> seedGrad = zg =>
{
    if (zg == null) return;
    zg.stops.Clear();
    zg.stops.Add(new ZuiGradientStop { pos = 0f, color = UnityEngine.Color.yellow });
    zg.stops.Add(new ZuiGradientStop { pos = 1f, color = new UnityEngine.Color(0f, 0.2f, 1f, 1f) });
};
System.Action<Laubrary.Shaper.ShaperFillDef> prepFill = fd =>
{
    if (fd == null) return;
    fd.texture = tex; fd.heightField = hf;
    seedGrad(fd.gradient); seedGrad(fd.rampGradient);
    seedGrad(fd.proceduralGradient); seedGrad(fd.overPhaseGradient);
};

sb.Append("doc\tkind\tcard\tcontrol\ttype\tstate\tvalue\tpixels\n");

for (int di = 0; di < 3; di++)
{
    string docLabel = di == 0 ? "Star" : di == 1 ? "Pyramid" : "Bag (2 members)";
    string path = dir + "/Sweep" + di + ".asset";
    UnityEditor.AssetDatabase.DeleteAsset(path);
    var d0 = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d0.canvasWidth = 96; d0.canvasHeight = 64; d0.frameCount = 16; d0.seed = 3u;
    d0.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    var lay = new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, id = 1,
                                               root = new Laubrary.Shaper.ShaperNode { name = "N" } };
    var nd = lay.root;
    if (di == 0)
    {
        nd.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
        nd.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star };
        nd.primitive.starRadiusDial = new ZUIValue(26f);
    }
    else if (di == 1)
    {
        nd.kind = Laubrary.Shaper.ShaperNodeKind.Solid;
        nd.solid = new Laubrary.Shaper.ShaperSolidDef { form = Laubrary.Shaper.ShaperSolidForm.Pyramid };
    }
    else
    {
        nd.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
        var m1 = new Laubrary.Shaper.ShaperNode { name = "m1", kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
        m1.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse };
        m1.primitive.ellipseRxDial = new ZUIValue(20f); m1.primitive.ellipseRyDial = new ZUIValue(16f);
        m1.transform.translateX = new ZUIValue(-10f);
        var m2 = new Laubrary.Shaper.ShaperNode { name = "m2", kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
        m2.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse };
        m2.primitive.ellipseRxDial = new ZUIValue(16f); m2.primitive.ellipseRyDial = new ZUIValue(20f);
        m2.transform.translateX = new ZUIValue(10f);
        nd.children.Add(m1); nd.children.Add(m2);
    }
    nd.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill();
    if (di != 1) nd.border = new Laubrary.Shaper.ShaperBorderDef { enabled = false };
    if (nd.border != null && nd.border.fill == null) nd.border.fill = new Laubrary.Shaper.ShaperFillDef();
    d0.layers.Add(lay);
    UnityEditor.AssetDatabase.CreateAsset(d0, path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(d0);
    UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
    var layer = doc.layers[0]; var node = layer.root;
    prepFill(node.fill); if (node.border != null) prepFill(node.border.fill);

    System.Func<int, UnityEngine.Color32[]> render = fi => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, fi);
    System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) =>
    { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

    UnityEngine.Color32[] b0 = null, bM = null, bL = null;
    string curState = null;
    System.Action<string> applyState = key =>
    {
        if (node.border != null) node.border.enabled = false;
        node.fill.kind = Laubrary.Shaper.ShaperFillKind.Solid;
        if (node.border != null && node.border.fill != null)
            node.border.fill.kind = Laubrary.Shaper.ShaperFillKind.Solid;
        bool edge = key.StartsWith("Edge/fill/");
        var target = edge ? (node.border != null ? node.border.fill : null) : node.fill;
        if (edge && node.border != null) { node.border.enabled = true; node.border.width = new ZUIValue(3f); }
        string spec = edge ? key.Substring("Edge/fill/".Length) : key.Substring("Fill/".Length);
        var bits = spec.Split('/');
        if (target != null)
        {
            target.kind = (Laubrary.Shaper.ShaperFillKind)System.Enum.Parse(typeof(Laubrary.Shaper.ShaperFillKind), bits[0]);
            if (bits.Length > 1)
            {
                if (bits[0] == "Gradient")
                    target.gradientMode = (Laubrary.Shaper.ShaperGradientMode)System.Enum.Parse(typeof(Laubrary.Shaper.ShaperGradientMode), bits[1]);
                else if (bits[0] == "Procedural")
                    target.proceduralKind = (Laubrary.Shaper.ShaperProceduralKind)System.Enum.Parse(typeof(Laubrary.Shaper.ShaperProceduralKind), bits[1]);
            }
        }
        b0 = render(0); bM = render(8); bL = render(15);
        curState = key;
    };

    // which state a fill field belongs to - the same routing T-0265 used
    System.Func<string, string> stateOf = name =>
    {
        if (name == "solidColor") return "Solid";
        if (name.StartsWith("stripSlots") || name.StartsWith("strip")) return "IndexedStrip";
        if (name.StartsWith("gradientDepthPixels")) return "Gradient/ByEdgeDistance";
        if (name.StartsWith("gradientCentre") || name == "gradientSize") return "Gradient/Radial";
        if (name.StartsWith("gradient")) return "Gradient";
        if (name.StartsWith("ramp")) return "RampByQuantity";
        if (name.StartsWith("texture")) return "Texture";
        if (name.StartsWith("heightField")) return "HeightField";
        if (name.StartsWith("steel")) return "TapestrySteel";
        if (name.StartsWith("overPhase")) return "OverPhase";
        if (name.StartsWith("dot")) return "Procedural/Dots";
        if (name.StartsWith("grid")) return "Procedural/Grid";
        if (name.StartsWith("procedural") || name == "noiseKind") return "Procedural";
        return "Gradient";   // the shared dials, measured on a kind that reads a position
    };

    var floatCands = new float[] { 0f, 16f, 1f, -16f, 64f };
    var fields = typeof(Laubrary.Shaper.ShaperFillDef).GetFields(PUB);

    System.Action<string, Laubrary.Shaper.ShaperFillDef, string> sweepOne = (card, fd, prefix) =>
    {
        if (fd == null) return;
        foreach (var fi in fields)
        {
            var ft = fi.FieldType;
            if (ft == typeof(UnityEngine.Texture2D)) continue;
            string name = fi.Name;
            string state = prefix + stateOf(name);
            if (state != curState) applyState(state);

            int best = 0; string bestVal = "";
            System.Func<System.Action, string, bool> probe = (apply, label) =>
            { try { apply(); int d = diff(bM, render(8)); if (d > best) { best = d; bestVal = label; }
                    if (d == 0) { int e = System.Math.Max(diff(b0, render(0)), diff(bL, render(15)));
                                  if (e > best) { best = e; bestVal = label + "@ends"; } }
                    return best > 0; }
              catch { return false; } };

            if (ft == typeof(ZUIValue))
            {
                var v = (ZUIValue)fi.GetValue(fd);
                if (v == null) { v = new ZUIValue(0f); fi.SetValue(fd, v); }
                var om = v.mode; float ov = v.staticValue; v.mode = ZUIValue.Mode.Static;
                foreach (var c in floatCands)
                { if (UnityEngine.Mathf.Approximately(c, ov)) continue; var cc = c;
                  if (probe(() => v.staticValue = cc, cc.ToString())) break; }
                v.mode = om; v.staticValue = ov;
            }
            else if (ft == typeof(bool))
            {
                bool ov = (bool)fi.GetValue(fd);
                probe(() => fi.SetValue(fd, !ov), (!ov).ToString());
                fi.SetValue(fd, ov);
            }
            else if (ft.IsEnum)
            {
                var ov = fi.GetValue(fd); int tried = 0;
                foreach (var vv in System.Enum.GetValues(ft))
                { if (vv.Equals(ov)) continue; if (tried++ >= 5) break; var cap = vv;
                  if (probe(() => fi.SetValue(fd, cap), cap.ToString())) break; }
                fi.SetValue(fd, ov);
            }
            else if (ft == typeof(UnityEngine.Color))
            {
                var ov = (UnityEngine.Color)fi.GetValue(fd);
                if (!probe(() => fi.SetValue(fd, new UnityEngine.Color(1f, 0f, 1f, 1f)), "magenta"))
                    probe(() => fi.SetValue(fd, new UnityEngine.Color(0f, 1f, 0f, 0f)), "green a=0");
                fi.SetValue(fd, ov);
            }
            else if (ft == typeof(ZuiGradient))
            {
                var zg = (ZuiGradient)fi.GetValue(fd);
                if (zg != null && zg.stops != null && zg.stops.Count > 0)
                {
                    var oc = zg.stops[0].color;
                    probe(() => zg.stops[0].color = UnityEngine.Color.magenta, "stop0=magenta");
                    zg.stops[0].color = oc;
                }
            }
            else if (ft.IsGenericType && name == "stripSlots")
            {
                var slots = fd.stripSlots;
                for (int si = 0; slots != null && si < slots.Count; si++)
                {
                    var slot = slots[si]; int cap = si;
                    int b1 = 0; string v1 = "";
                    var oc = slot.color;
                    { slot.color = UnityEngine.Color.magenta; int d = diff(bM, render(8));
                      if (d > b1) { b1 = d; v1 = "magenta"; } slot.color = oc; }
                    sb.Append(docLabel).Append('\t').Append(node.kind).Append('\t').Append(card)
                      .Append("\tstripSlots[").Append(cap).Append("].color\tColor\t").Append(state)
                      .Append('\t').Append(v1).Append('\t').Append(b1).Append('\n');
                    int b2 = 0; string v2 = ""; float oh = slot.height;
                    foreach (var c in floatCands)
                    { if (UnityEngine.Mathf.Approximately(c, oh)) continue; slot.height = c;
                      int d = System.Math.Max(diff(bM, render(8)),
                              System.Math.Max(diff(b0, render(0)), diff(bL, render(15))));
                      if (d > b2) { b2 = d; v2 = c.ToString(); }
                      if (b2 > 0) break; }
                    slot.height = oh;
                    sb.Append(docLabel).Append('\t').Append(node.kind).Append('\t').Append(card)
                      .Append("\tstripSlots[").Append(cap).Append("].height\tSingle\t").Append(state)
                      .Append('\t').Append(v2).Append('\t').Append(b2).Append('\n');
                }
                continue;
            }
            else continue;

            sb.Append(docLabel).Append('\t').Append(node.kind).Append('\t').Append(card).Append('\t')
              .Append(name).Append('\t').Append(ft.Name).Append('\t').Append(state).Append('\t')
              .Append(bestVal).Append('\t').Append(best).Append('\n');
        }
    };

    sweepOne("Fill", node.fill, "Fill/");
    if (node.border != null) sweepOne("Edge (border)", node.border.fill, "Edge/fill/");
    UnityEditor.AssetDatabase.DeleteAsset(path);
}

UnityEngine.Object.DestroyImmediate(tex);
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0326\out\fill-sweep-t326.tsv", sb.ToString());
return "rows=" + (sb.ToString().Split('\n').Length - 2);

