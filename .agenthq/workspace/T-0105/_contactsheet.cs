// T-0105 visual contact sheet: rasterise every primitive + operator + a composed tree to one PNG.
// Run via: unity command --project-path "D:/UNITY/Laubrary Dev - Shaper" eval_file --file <this>

int CELL = 128, COLS = 6, ROWS = 4;
float half = 70f; // canvas half-extent per cell

var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Laubrary.Shaper.ShaperNode>>();
System.Action<string, Laubrary.Shaper.ShaperNode> add = (n, node) =>
    items.Add(new System.Collections.Generic.KeyValuePair<string, Laubrary.Shaper.ShaperNode>(n, node));

// --- the seven primitives -----------------------------------------------------------------
add("Rect",     Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 55f, rectHalfH = 38f, rectCornerRadius = 10f }));
add("Ellipse",  Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = 58f, ellipseRy = 30f }));
add("Diamond",  Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Diamond, diamondRx = 55f, diamondRy = 45f }));
add("Triangle", Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Triangle, triangleBase = 100f, triangleHeight = 95f }));
add("Capsule",  Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Capsule, capsuleHalfLength = 38f, capsuleRadius = 18f }));
add("NGon 6",   Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 55f }));

// --- N-gon family: the true regular polygon that replaces the hand-fitted hex/oct ----------
add("NGon 3",   Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.NGon, ngonSides = 3, ngonRadius = 55f }));
add("NGon 8",   Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.NGon, ngonSides = 8, ngonRadius = 55f }));
add("NGon 6 rounded", Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 55f, ngonCornerRadius = 18f }));

// --- the parameterised star, swept over Pyre's dials ---------------------------------------
add("Star default",   Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 5,  starRadius = 58f }));
add("Star 7 long",    Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 7,  starRadius = 58f, starLength = new ZUIValue(0.85f) }));
add("Star bw 0.35",   Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 6,  starRadius = 58f, starLength = new ZUIValue(0.75f), starBaseWidth = new ZUIValue(0.35f) }));
add("Star skew +45",  Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 6,  starRadius = 58f, starLength = new ZUIValue(0.75f), starBaseWidth = new ZUIValue(0.35f), starSkew = new ZUIValue(45f) }));
add("Star 14 arms",   Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 14, starRadius = 58f, starLength = new ZUIValue(0.55f) }));
add("Star bw 1.0",    Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 5,  starRadius = 58f, starLength = new ZUIValue(0.9f), starBaseWidth = new ZUIValue(1f) }));

// --- sweep, on both declared axes ----------------------------------------------------------
System.Func<Laubrary.Shaper.ShaperNode, float, float, Laubrary.Shaper.ShaperNode> sweepR = (n, s, e) =>
    { n.sweep = new Laubrary.Shaper.ShaperSweep { enabled = true, startDegrees = s, extentDegrees = e }; return n; };

add("Sweep 90 on disc",   sweepR(Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = 58f, ellipseRy = 58f }), 20f, 90f));
add("Sweep 260 on star",  sweepR(Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 6, starRadius = 58f, starLength = new ZUIValue(0.7f) }), 0f, 260f));

var cap = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Capsule, capsuleHalfLength = 55f, capsuleRadius = 16f });
cap.sweep = new Laubrary.Shaper.ShaperSweep { enabled = true, startFraction = 0.15f, extentFraction = 0.5f };
add("Sweep along capsule", cap);

// --- shell, all three alignments -----------------------------------------------------------
System.Func<Laubrary.Shaper.ShaperPrimitiveDef, Laubrary.Shaper.ShaperShellAlignment, float, Laubrary.Shaper.ShaperNode> shell = (d, a, t) =>
    { var n = Laubrary.Shaper.ShaperNode.Primitive(d); n.shell = new Laubrary.Shaper.ShaperShell { enabled = true, thickness = t, alignment = a }; return n; };

add("Shell centred (disc)", shell(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = 55f, ellipseRy = 55f }, Laubrary.Shaper.ShaperShellAlignment.Centred, 10f));
add("Shell on star",        shell(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 5, starRadius = 58f, starLength = new ZUIValue(0.7f) }, Laubrary.Shaper.ShaperShellAlignment.Centred, 9f));
add("Shell inward (ngon)",  shell(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.NGon, ngonSides = 5, ngonRadius = 55f }, Laubrary.Shaper.ShaperShellAlignment.Inward, 12f));

// --- a composed tree: bag with a soft add and a subtract, under a transform -----------------
var a1 = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = 34f, ellipseRy = 34f }, "a", Laubrary.Shaper.ShaperCombineMode.Add);
a1.transform.translate = new Vector2(-22f, 0f);
var a2 = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = 34f, ellipseRy = 34f }, "b", Laubrary.Shaper.ShaperCombineMode.Add);
a2.transform.translate = new Vector2(22f, 0f);
a2.blend.width = 26f;
var a3 = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Star, starArms = 5, starRadius = 26f, starLength = new ZUIValue(0.7f) }, "cut", Laubrary.Shaper.ShaperCombineMode.Subtract);
add("Bag: soft add + star cut", Laubrary.Shaper.ShaperNode.Bag("bag", Laubrary.Shaper.ShaperCombineMode.Add, a1, a2, a3));

// =================================================================================================
var tex = new Texture2D(CELL * COLS, CELL * ROWS, TextureFormat.RGBA32, false);
var px  = new Color32[CELL * COLS * CELL * ROWS];
for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 26, 30, 255);

var log = new System.Text.StringBuilder();
for (int idx = 0; idx < items.Count && idx < COLS * ROWS; idx++)
{
    int cx = idx % COLS, cy = idx / COLS;
    var prog  = Laubrary.Shaper.ShaperCompiler.Compile(items[idx].Value);
    var stack = prog.NewStack();
    float pixelSize = (half * 2f) / CELL;
    var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(CELL, CELL, pixelSize, 0f);

    int nan = 0;
    for (int iy = 0; iy < CELL; iy++)
        for (int ix = 0; ix < CELL; ix++)
        {
            float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog, grid.X(ix), grid.Y(iy), stack);
            if (float.IsNaN(d)) { nan++; continue; }
            float cov = Laubrary.Shaper.ShaperField.Coverage(d, Laubrary.Shaper.ShaperField.HalfBand(0f, pixelSize));
            // tint by inside/outside so the sign convention is visible too
            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(cov) * 255f);
            int destX = cx * CELL + ix;
            int destY = (ROWS - 1 - cy) * CELL + iy;          // row 0 at the top of the sheet
            var bg = new Color32(24, 26, 30, 255);
            var fg = new Color32(235, 226, 208, 255);
            px[destY * CELL * COLS + destX] = new Color32(
                (byte)((bg.r * (255 - v) + fg.r * v) / 255),
                (byte)((bg.g * (255 - v) + fg.g * v) / 255),
                (byte)((bg.b * (255 - v) + fg.b * v) / 255), 255);
        }
    log.AppendLine(string.Format("{0,2}. {1,-24} bound={2:F3}  nan={3}", idx + 1, items[idx].Key, prog.bound, nan));
}

// 1px grid lines so the cells are readable
for (int c = 1; c < COLS; c++)
    for (int y = 0; y < CELL * ROWS; y++) px[y * CELL * COLS + c * CELL] = new Color32(70, 74, 82, 255);
for (int r = 1; r < ROWS; r++)
    for (int x = 0; x < CELL * COLS; x++) px[(r * CELL) * CELL * COLS + x] = new Color32(70, 74, 82, 255);

tex.SetPixels32(px);
tex.Apply();
string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0105\shaper-contact-sheet.png";
System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(tex);

return "WROTE " + outPath + "\n" + log.ToString();
