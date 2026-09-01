// T-0109 second fix pass — the rim question, measured where the POINT lamp actually is.
// The contact sheet's rig is a directional key PLUS a point lamp at (26, -20, 30) range 70, so
// `pointZ` (= base + height) is live in the picture and the full-`body` cut at the antialiased rim
// IS exercised. This finds the worst rim in the sheet and magnifies it.
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
string[] nm = { "01 Flat","02 Linear45","03 Stepped","04 Dome","05 Round","06 Taper","07 Pyramid",
                "08 Flat+Rnd","09 Lin+Rnd","10 Step+Rnd","11 Dome+Rnd","12 Round+Rnd","13 Taper+Rnd","14 Pyr+Rnd",
                "15 bev None","16 bev Linear","17 bev Rounded","18 bev Cove","19 bev Ogee","20 bev Stepped",
                "21 Stepped12","22 Dome.25","23 Round4","24 Taper.3","25 StarDome","26 StarStep","27 depth0","28 Linear135" };
bool[] noBevel = { true,true,true,true,true,true,true, false,false,false,false,false,false,false,
                   true,false,false,false,false,false, true,true,true,true,true,false,true,true };

var sb = new System.Text.StringBuilder();
sb.AppendLine("Rig: directional key (yaw -55, pitch 34) PLUS a POINT lamp at (26,-20,30) range 70.");
sb.AppendLine("So pointZ = base + height IS read by the shading, and FillTile's rim cut IS in the picture.");
sb.AppendLine("The point lamp is to the lower-RIGHT of each cell's centre, so the RIGHT and BOTTOM rims");
sb.AppendLine("are where a pointZ artefact would show.");
sb.AppendLine();
sb.AppendLine(string.Format("{0,-16} {1,-8} {2,10} {3,10} {4,10} {5,10}", "cell", "bevel?", "R-jump", "R-band", "B-jump", "B-band"));

float worstJump = 0f; int worstCell = -1; string worstSide = "";
for (int ci = 0; ci < 28; ci++)
{
    int ox = Pad + (ci % 6) * (Cell + Pad), oy = Pad + (ci / 6) * (Cell + Pad);
    float bg = L(ox + 2, oy + 2);

    // RIGHT rim along the mid scanline, walking inward from the right edge.
    float rJump = 0f; int rBand = 0;
    {
        int y = oy + Cell / 2, rim = -1;
        for (int x = ox + Cell - 3; x > ox + 2; x--)
            if (UnityEngine.Mathf.Abs(L(x, y) - bg) > 0.02f) { rim = x; break; }
        if (rim > 0)
        {
            float inner = L(rim - 10 < ox ? ox : rim - 10, y);
            for (int x = rim + 1; x >= rim - 12 && x > ox; x--)
            {
                float j = UnityEngine.Mathf.Abs(L(x, y) - L(x + 1, y));
                if (j > rJump) rJump = j;
                float f = (L(x, y) - bg) / UnityEngine.Mathf.Max(1e-6f, inner - bg);
                if (f > 0.05f && f < 0.95f) rBand++;
            }
            if (rJump > worstJump) { worstJump = rJump; worstCell = ci; worstSide = "right"; }
        }
    }
    // BOTTOM rim along the mid column.
    float bJump = 0f; int bBand = 0;
    {
        int x = ox + Cell / 2, rim = -1;
        for (int y = oy + Cell - 3; y > oy + 2; y--)
            if (UnityEngine.Mathf.Abs(L(x, y) - bg) > 0.02f) { rim = y; break; }
        if (rim > 0)
        {
            float inner = L(x, rim - 10 < oy ? oy : rim - 10);
            for (int y = rim + 1; y >= rim - 12 && y > oy; y--)
            {
                float j = UnityEngine.Mathf.Abs(L(x, y) - L(x, y + 1));
                if (j > bJump) bJump = j;
                float f = (L(x, y) - bg) / UnityEngine.Mathf.Max(1e-6f, inner - bg);
                if (f > 0.05f && f < 0.95f) bBand++;
            }
            if (bJump > worstJump) { worstJump = bJump; worstCell = ci; worstSide = "bottom"; }
        }
    }
    sb.AppendLine(string.Format("{0,-16} {1,-8} {2,10:F4} {3,10} {4,10:F4} {5,10}",
                  nm[ci], noBevel[ci] ? "NONE" : "yes", rJump, rBand, bJump, bBand));
}
sb.AppendLine();
sb.AppendLine("worst single-pixel luminance jump anywhere on a rim: " + worstJump.ToString("F4") +
              "  at " + (worstCell >= 0 ? nm[worstCell] : "-") + " (" + worstSide + ")");

// magnify the worst rim
if (worstCell >= 0)
{
    int ox = Pad + (worstCell % 6) * (Cell + Pad), oy = Pad + (worstCell / 6) * (Cell + Pad);
    float bg = L(ox + 2, oy + 2);
    int cx, cy;
    if (worstSide == "right")
    {
        cy = oy + Cell / 2; cx = ox + Cell - 3;
        for (int x = ox + Cell - 3; x > ox + 2; x--) if (UnityEngine.Mathf.Abs(L(x, cy) - bg) > 0.02f) { cx = x; break; }
    }
    else
    {
        cx = ox + Cell / 2; cy = oy + Cell - 3;
        for (int y = oy + Cell - 3; y > oy + 2; y--) if (UnityEngine.Mathf.Abs(L(cx, y) - bg) > 0.02f) { cy = y; break; }
    }
    const int Zoom = 10, Crop = 24;
    int outW = Crop * Zoom, outH = Crop * Zoom;
    var t2 = new UnityEngine.Texture2D(outW, outH, UnityEngine.TextureFormat.RGBA32, false);
    var buf = new UnityEngine.Color32[outW * outH];
    int sx = UnityEngine.Mathf.Clamp(cx - Crop / 2, 0, W - Crop), sy = UnityEngine.Mathf.Clamp(cy - Crop / 2, 0, H - Crop);
    for (int y = 0; y < outH; y++)
        for (int x = 0; x < outW; x++)
            buf[(outH - 1 - y) * outW + x] = P(sx + x / Zoom, sy + y / Zoom);
    t2.SetPixels32(buf); t2.Apply();
    System.IO.File.WriteAllBytes(dir + "rim-step.png", t2.EncodeToPNG());
    sb.AppendLine("wrote rim-step.png: " + outW + "x" + outH + ", 10x nearest-neighbour crop of " +
                  nm[worstCell] + "'s " + worstSide + " rim, centred on the sheet pixel (" + cx + "," + cy + ").");
}

return sb.ToString();
