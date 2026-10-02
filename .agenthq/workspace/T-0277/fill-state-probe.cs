// T-0277 - the dead fill dials, each measured in the state the sweep used AND in the state the code says
// it needs. Documents are built, written to disk, re-imported and re-loaded, exactly as T-0265 did.
var AD = typeof(UnityEditor.AssetDatabase);
var sb = new System.Text.StringBuilder();
string dir = "Assets/Shaper/Audit0277";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper"))
    UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "Audit0277");

// ── a real sprite-sheet asset: 4 columns x 1 row of 8x8 cells, each a different colour ──────────────
string sheetPath = dir + "/sheet0277.png";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(sheetPath) == null)
{
    var t = new UnityEngine.Texture2D(32, 8, UnityEngine.TextureFormat.RGBA32, false);
    var cols = new UnityEngine.Color[] { UnityEngine.Color.red, UnityEngine.Color.green,
                                         UnityEngine.Color.blue, UnityEngine.Color.yellow };
    for (int y = 0; y < 8; y++) for (int x = 0; x < 32; x++)
    {
        var c = cols[x / 8];
        // a diagonal inside each cell so a TILED repeat is distinguishable from a fitted stretch
        if (((x % 8) + y) % 4 == 0) c = UnityEngine.Color.white;
        t.SetPixel(x, y, c);
    }
    t.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), sheetPath),
                                 t.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(t);
    UnityEditor.AssetDatabase.ImportAsset(sheetPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(sheetPath);
    imp.isReadable = true; imp.filterMode = UnityEngine.FilterMode.Point;
    imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    imp.npotScale = UnityEditor.TextureImporterNPOTScale.None;
    imp.mipmapEnabled = false;
    imp.SaveAndReimport();
}
var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(sheetPath);

// ── a height field asset: a horizontal ramp, so its gradient is non-zero and a scale on it tilts ────
string hfPath = dir + "/hfield0277.png";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(hfPath) == null)
{
    var t = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBA32, false);
    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
    {
        float v = UnityEngine.Mathf.Abs(x - 16) / 16f;
        t.SetPixel(x, y, new UnityEngine.Color(v, v, v, 1f));
    }
    t.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), hfPath),
                                 t.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(t);
    UnityEditor.AssetDatabase.ImportAsset(hfPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(hfPath);
    imp.isReadable = true; imp.filterMode = UnityEngine.FilterMode.Point;
    imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    imp.npotScale = UnityEditor.TextureImporterNPOTScale.None;
    imp.mipmapEnabled = false;
    imp.SaveAndReimport();
}
var hfield = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(dir + "/rfield0277.asset");

// ── the document: a WIDE ellipse (non-square anchor box) under a smaller overlapping second layer ───
string docPath = dir + "/AuditFill.asset";
UnityEditor.AssetDatabase.DeleteAsset(docPath);
var d0 = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d0.canvasWidth = 96; d0.canvasHeight = 64; d0.frameCount = 16; d0.seed = 7u;
d0.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });

var la = new Laubrary.Shaper.ShaperLayer { name = "A", enabled = true, id = 1,
                             root = new Laubrary.Shaper.ShaperNode { name = "Wide" } };
la.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
la.root.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse };
la.root.primitive.ellipseRxDial = new ZUIValue(34f);
la.root.primitive.ellipseRyDial = new ZUIValue(13f);
la.root.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill();
d0.layers.Add(la);

var lb = new Laubrary.Shaper.ShaperLayer { name = "B", enabled = true, id = 2,
                             root = new Laubrary.Shaper.ShaperNode { name = "Small" } };
lb.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
lb.root.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse };
lb.root.primitive.ellipseRxDial = new ZUIValue(12f);
lb.root.primitive.ellipseRyDial = new ZUIValue(12f);
lb.root.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill();
lb.root.fill.solidColor = new UnityEngine.Color(0f, 0.6f, 1f, 1f);
d0.layers.Add(lb);

// a third document layer set is not needed: B overlaps A at the centre, which is what the depth
// composite needs in order to have an ordering to decide.
UnityEditor.AssetDatabase.CreateAsset(d0, docPath);
UnityEditor.AssetDatabase.SaveAssetIfDirty(d0);
UnityEditor.AssetDatabase.ImportAsset(docPath, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(docPath);
var A = doc.layers[0]; var B = doc.layers[1];
var f = A.root.fill;
f.texture = sheet; f.heightField = hfield;

System.Func<int, UnityEngine.Color32[]> render = fi => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, fi);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

UnityEngine.Color32[] p0 = null, p8 = null, p15 = null;
System.Action snap = () => { p0 = render(0); p8 = render(8); p15 = render(15); };
// state -> then measure: apply, render three frames, max diff
System.Func<System.Action, int> delta = apply =>
{ apply(); int a = diff(p0, render(0)), b = diff(p8, render(8)), c = diff(p15, render(15));
  return System.Math.Max(a, System.Math.Max(b, c)); };

System.Action<Laubrary.Shaper.ShaperFillDef> reset = fd =>
{
    fd.kind = Laubrary.Shaper.ShaperFillKind.Solid;
    fd.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
    fd.quantiseLevels = new ZUIValue(0f);
    fd.heightDelta = new ZUIValue(0f);
    fd.veil = new ZUIValue(1f);
    fd.textureMapping = Laubrary.Shaper.ShaperTextureMapping.Fitted;
    fd.textureTilesX = new ZUIValue(1f); fd.textureTilesY = new ZUIValue(1f);
    fd.textureAnimated = false;
    fd.textureFrameColumns = new ZUIValue(1f); fd.textureFrameRows = new ZUIValue(1f);
    fd.textureFrameCount = new ZUIValue(1f);
    fd.stripOffset = new ZUIValue(0f); fd.stripReach = new ZUIValue(1f);
    fd.stripPlainColor = UnityEngine.Color.white;
    fd.heightFieldScale = new ZUIValue(1f);
    fd.gradientTint = UnityEngine.Color.white; fd.rampTint = UnityEngine.Color.white;
    fd.overPhaseTint = UnityEngine.Color.white; fd.proceduralTint = UnityEngine.Color.white;
    fd.proceduralKind = Laubrary.Shaper.ShaperProceduralKind.Noise;
    fd.gradientMode = Laubrary.Shaper.ShaperGradientMode.Radial;
    if (fd.stripSlots != null)
        for (int i = 0; i < fd.stripSlots.Count; i++) fd.stripSlots[i].height = 0f;
};

System.Action<ZuiGradient> seedGrad = zg =>
{
    if (zg == null) return;
    zg.stops.Clear();
    zg.stops.Add(new ZuiGradientStop { pos = 0f, color = UnityEngine.Color.yellow });
    zg.stops.Add(new ZuiGradientStop { pos = 1f, color = new UnityEngine.Color(0f, 0.2f, 1f, 1f) });
};
System.Func<ZuiGradient> mkGrad = () => { var g = new ZuiGradient(); seedGrad(g); return g; };

sb.Append("dial\tstate\tpixels\n");
System.Action<string, string, int> row = (dial, state, px) =>
    sb.Append(dial).Append('\t').Append(state).Append('\t').Append(px).Append('\n');

// ══ 1. Fit ══════════════════════════════════════════════════════════════════════════════════════════
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Gradient; f.gradientMode = Laubrary.Shaper.ShaperGradientMode.Radial;
f.gradient = mkGrad();
snap();
row("fit", "Gradient/Radial on a WIDE ellipse (34x13)", delta(() => f.fit = Laubrary.Shaper.ShaperFillFit.Stretch));
f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
// square shape: the same choice on an equal-axis ellipse
A.root.primitive.ellipseRyDial = new ZUIValue(34f);
snap();
row("fit", "Gradient/Radial on a SQUARE ellipse (34x34)", delta(() => f.fit = Laubrary.Shaper.ShaperFillFit.Stretch));
f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
A.root.primitive.ellipseRyDial = new ZUIValue(13f);
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Solid; snap();
row("fit", "Solid (no anchor read)", delta(() => f.fit = Laubrary.Shaper.ShaperFillFit.Stretch));
f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;

// ══ 2. Posterise ════════════════════════════════════════════════════════════════════════════════════
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.TapestrySteel; snap();
row("quantiseLevels", "TapestrySteel", delta(() => f.quantiseLevels = new ZUIValue(3f)));
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Gradient; f.gradientMode = Laubrary.Shaper.ShaperGradientMode.Radial;
f.gradient = mkGrad(); snap();
row("quantiseLevels", "Gradient", delta(() => f.quantiseLevels = new ZUIValue(3f)));
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; snap();
row("quantiseLevels", "Texture", delta(() => f.quantiseLevels = new ZUIValue(3f)));

// ══ 3. Texture tiles / animation ════════════════════════════════════════════════════════════════════
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; f.textureMapping = Laubrary.Shaper.ShaperTextureMapping.Fitted; snap();
row("textureTilesX", "Texture, Mapping=Fit once", delta(() => f.textureTilesX = new ZUIValue(4f)));
f.textureTilesX = new ZUIValue(1f);
row("textureTilesY", "Texture, Mapping=Fit once", delta(() => f.textureTilesY = new ZUIValue(4f)));
f.textureTilesY = new ZUIValue(1f);
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; f.textureMapping = Laubrary.Shaper.ShaperTextureMapping.Tiled; snap();
row("textureTilesX", "Texture, Mapping=Repeat", delta(() => f.textureTilesX = new ZUIValue(4f)));
f.textureTilesX = new ZUIValue(1f);
row("textureTilesY", "Texture, Mapping=Repeat", delta(() => f.textureTilesY = new ZUIValue(4f)));
f.textureTilesY = new ZUIValue(1f);

reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; snap();
row("textureAnimated", "on, Columns=1 Rows=1 (the defaults)", delta(() => f.textureAnimated = true));
f.textureAnimated = false;
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; f.textureAnimated = true; snap();
row("textureFrameColumns", "Animated on", delta(() => f.textureFrameColumns = new ZUIValue(4f)));
f.textureFrameColumns = new ZUIValue(1f);
row("textureFrameRows", "Animated on", delta(() => f.textureFrameRows = new ZUIValue(4f)));
f.textureFrameRows = new ZUIValue(1f);
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; f.textureAnimated = true;
f.textureFrameColumns = new ZUIValue(4f); f.textureFrameCount = new ZUIValue(4f); snap();
row("textureFrameCount", "Animated on, Columns=4, Frames 4->2", delta(() => f.textureFrameCount = new ZUIValue(2f)));
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Texture; f.textureAnimated = true; snap();
row("textureFrameCount", "Animated on, Columns=1 Rows=1", delta(() => f.textureFrameCount = new ZUIValue(4f)));

// ══ 4. Strip offset / plain colour / slot height ════════════════════════════════════════════════════
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.IndexedStrip; snap();
row("stripOffset", "IndexedStrip, offset 0->16 (a whole turn)", delta(() => f.stripOffset = new ZUIValue(16f)));
f.stripOffset = new ZUIValue(0f);
row("stripOffset", "IndexedStrip, offset 0->0.5", delta(() => f.stripOffset = new ZUIValue(0.5f)));
f.stripOffset = new ZUIValue(0f);
row("stripPlainColor", "IndexedStrip, Reach 1 (the default)",
    delta(() => f.stripPlainColor = UnityEngine.Color.magenta));
f.stripPlainColor = UnityEngine.Color.white;
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.IndexedStrip; f.stripReach = new ZUIValue(0.2f); snap();
row("stripPlainColor", "IndexedStrip, Reach 0.2",
    delta(() => f.stripPlainColor = UnityEngine.Color.magenta));
f.stripPlainColor = UnityEngine.Color.white;

reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.IndexedStrip; snap();
row("stripSlots[0].height", "IndexedStrip, two layers, spacing 0.75",
    delta(() => { if (f.stripSlots != null && f.stripSlots.Count > 0) f.stripSlots[0].height = 24f; }));
if (f.stripSlots != null && f.stripSlots.Count > 0) f.stripSlots[0].height = 0f;
B.enabled = false; snap();
row("stripSlots[0].height", "IndexedStrip, layer B off (one layer)",
    delta(() => { if (f.stripSlots != null && f.stripSlots.Count > 0) f.stripSlots[0].height = 24f; }));
if (f.stripSlots != null && f.stripSlots.Count > 0) f.stripSlots[0].height = 0f;
B.enabled = true;

// ══ 5. Height change / height-field scale ═══════════════════════════════════════════════════════════
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Solid; snap();
row("heightDelta", "Solid, two layers, spacing 0.75", delta(() => f.heightDelta = new ZUIValue(24f)));
f.heightDelta = new ZUIValue(0f);
B.enabled = false; snap();
row("heightDelta", "Solid, layer B off (one layer)", delta(() => f.heightDelta = new ZUIValue(24f)));
f.heightDelta = new ZUIValue(0f); B.enabled = true;

reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.HeightField; snap();
row("heightFieldScale", "HeightField on a PRIMITIVE, two layers",
    delta(() => f.heightFieldScale = new ZUIValue(6f)));
f.heightFieldScale = new ZUIValue(1f);
B.enabled = false; snap();
row("heightFieldScale", "HeightField on a PRIMITIVE, one layer",
    delta(() => f.heightFieldScale = new ZUIValue(6f)));
f.heightFieldScale = new ZUIValue(1f); B.enabled = true;

// the same dial on a SOLID, which perturbs its own analytic normal by a HeightField fill (T-0127)
A.root.kind = Laubrary.Shaper.ShaperNodeKind.Solid;
A.root.solid = new Laubrary.Shaper.ShaperSolidDef { form = Laubrary.Shaper.ShaperSolidForm.Pyramid };
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.HeightField; B.enabled = false; snap();
row("heightFieldScale", "HeightField on a SOLID (Pyramid), one layer",
    delta(() => f.heightFieldScale = new ZUIValue(6f)));
f.heightFieldScale = new ZUIValue(1f);
row("heightDelta", "Solid (Pyramid) + HeightField fill, one layer",
    delta(() => f.heightDelta = new ZUIValue(24f)));
f.heightDelta = new ZUIValue(0f);
B.enabled = true;
A.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;

// ══ 6. The four tints ═══════════════════════════════════════════════════════════════════════════════
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Gradient; f.gradient = null; snap();
row("gradientTint", "Gradient, NO ramp authored", delta(() => f.gradientTint = UnityEngine.Color.magenta));
f.gradientTint = UnityEngine.Color.white;
f.gradient = mkGrad(); snap();
row("gradientTint", "Gradient, ramp authored", delta(() => f.gradientTint = UnityEngine.Color.magenta));
f.gradientTint = UnityEngine.Color.white;

reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.RampByQuantity; f.rampGradient = null; snap();
row("rampTint", "Ramp, NO ramp authored", delta(() => f.rampTint = UnityEngine.Color.magenta));
f.rampTint = UnityEngine.Color.white;
f.rampGradient = mkGrad(); snap();
row("rampTint", "Ramp, ramp authored", delta(() => f.rampTint = UnityEngine.Color.magenta));
f.rampTint = UnityEngine.Color.white;

reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.OverPhase; f.overPhaseGradient = null; snap();
row("overPhaseTint", "Over phase, NO ramp authored", delta(() => f.overPhaseTint = UnityEngine.Color.magenta));
f.overPhaseTint = UnityEngine.Color.white;
f.overPhaseGradient = mkGrad(); snap();
row("overPhaseTint", "Over phase, ramp authored", delta(() => f.overPhaseTint = UnityEngine.Color.magenta));
f.overPhaseTint = UnityEngine.Color.white;

reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Procedural; f.proceduralKind = Laubrary.Shaper.ShaperProceduralKind.Noise;
f.proceduralGradient = null; snap();
row("proceduralTint", "Procedural/Noise, NO ramp authored", delta(() => f.proceduralTint = UnityEngine.Color.magenta));
f.proceduralTint = UnityEngine.Color.white;
f.proceduralGradient = mkGrad(); snap();
row("proceduralTint", "Procedural/Noise, ramp authored", delta(() => f.proceduralTint = UnityEngine.Color.magenta));
f.proceduralTint = UnityEngine.Color.white;
reset(f); f.kind = Laubrary.Shaper.ShaperFillKind.Procedural; f.proceduralKind = Laubrary.Shaper.ShaperProceduralKind.Grid;
f.gridVertical = true; f.gridHorizontal = true; snap();
row("proceduralTint", "Procedural/Grid (the ink)", delta(() => f.proceduralTint = UnityEngine.Color.magenta));
f.proceduralTint = UnityEngine.Color.white;

System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0277\fill-after.tsv", sb.ToString());
return sb.ToString();
