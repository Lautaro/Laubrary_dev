// T-0109 second fix pass — the rim A/B the owner needs, as a picture.
//
// Three cells of the SAME re-rendered contact sheet, same rim, same magnification:
//   LEFT   27 depth 0        - NO height stage at all. The control: whatever rim you see here is the
//                              ordinary coverage antialias against the backdrop, nothing to do with height.
//   MIDDLE 01 Flat / None    - height cuts the FULL body across one pixel at the rim (the live behaviour).
//   RIGHT  17 Flat + Rounded - height RAMPS to 0 through the bevel band (what "ramping it" would look like).
//
// If LEFT and MIDDLE are indistinguishable, the cut is not visible in this artefact.
//
//   unity command --project-path "D:/UNITY/Laubrary Dev - Shaper" --timeout 900 eval_file --file <this>
string dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\";
byte[] png = System.IO.File.ReadAllBytes(dir + "height-contact-sheet.png");
var tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
tex.LoadImage(png);
int W = tex.width, H = tex.height;
var px = tex.GetPixels32();
System.Func<int, int, UnityEngine.Color32> P = (x, y) => px[(H - 1 - y) * W + x];
System.Func<int, int, float> L = (x, y) => { var c = P(x, y); return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f; };

const int Cell = 104, Pad = 5;
int[] cells = { 26, 0, 16 };
string[] labels = { "27 depth 0 (no height stage)", "01 Flat/None (full-body cut)", "17 Flat+Rounded (ramped)" };

var sb = new System.Text.StringBuilder();
sb.AppendLine("The RIGHT rim (the side the point lamp is on), mid scanline, luminance per pixel:");

const int Zoom = 10, Crop = 20;
int panelW = Crop * Zoom, gap = 10;
int outW = cells.Length * panelW + (cells.Length + 1) * gap, outH = panelW + 2 * gap;
var buf = new UnityEngine.Color32[outW * outH];
for (int i = 0; i < buf.Length; i++) buf[i] = new UnityEngine.Color32(18, 18, 22, 255);

var profiles = new System.Collections.Generic.List<float[]>();
for (int q = 0; q < cells.Length; q++)
{
    int ci = cells[q];
    int ox = Pad + (ci % 6) * (Cell + Pad), oy = Pad + (ci / 6) * (Cell + Pad);
    float bg = L(ox + 2, oy + 2);
    int y = oy + Cell / 2, rim = ox + Cell - 3;
    for (int x = ox + Cell - 3; x > ox + 2; x--) if (UnityEngine.Mathf.Abs(L(x, y) - bg) > 0.02f) { rim = x; break; }

    var row = new float[16];
    var line = new System.Text.StringBuilder();
    for (int i = 0; i < 16; i++) { int x = rim + 2 - i; row[i] = L(UnityEngine.Mathf.Clamp(x, 0, W - 1), y); line.Append(row[i].ToString("F3") + " "); }
    profiles.Add(row);
    sb.AppendLine("  " + labels[q].PadRight(30) + " rim x=" + rim + " : " + line.ToString());

    int sx = UnityEngine.Mathf.Clamp(rim - Crop / 2, 0, W - Crop), sy = UnityEngine.Mathf.Clamp(y - Crop / 2, 0, H - Crop);
    int dx0 = gap + q * (panelW + gap), dy0 = gap;
    for (int yy = 0; yy < panelW; yy++)
        for (int xx = 0; xx < panelW; xx++)
        {
            int tx = dx0 + xx, ty = dy0 + yy;
            if (tx < outW && ty < outH) buf[(outH - 1 - ty) * outW + tx] = P(sx + xx / Zoom, sy + yy / Zoom);
        }
}

// how different are the CONTROL and the LIVE cut, pixel for pixel, on that scanline?
float maxDiff = 0f;
for (int i = 0; i < 16; i++) maxDiff = UnityEngine.Mathf.Max(maxDiff, UnityEngine.Mathf.Abs(profiles[0][i] - profiles[1][i]));
sb.AppendLine();
sb.AppendLine("  max |luminance difference| between the CONTROL (no height at all) and the LIVE full-body cut,");
sb.AppendLine("  over the 16 rim pixels: " + maxDiff.ToString("F4") + "   (0.000 = the cut is invisible here)");

var t2 = new UnityEngine.Texture2D(outW, outH, UnityEngine.TextureFormat.RGBA32, false);
t2.SetPixels32(buf); t2.Apply();
System.IO.File.WriteAllBytes(dir + "rim-step.png", t2.EncodeToPNG());
sb.AppendLine("  wrote rim-step.png " + outW + "x" + outH + " (10x, left->right: control, live cut, ramped)");
return sb.ToString();
