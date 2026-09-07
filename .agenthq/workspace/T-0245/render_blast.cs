var spec = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>("Assets/Pyre/Imported/Driectional Blast Plus.asset");
if (spec == null) return "no spec";
Laubrary.Pyre.PyreRenderer.ClearFrameCache(spec);
var frames = Laubrary.Pyre.PyreRenderer.GetFrames(spec);
int n = frames.Length, w = spec.Width, h = spec.Height, scale = 4;
var sheet = new UnityEngine.Texture2D(n * w * scale + (n - 1) * 2 * scale, h * scale, UnityEngine.TextureFormat.RGBA32, false);
var fill = new UnityEngine.Color[sheet.width * sheet.height];
for (int i = 0; i < fill.Length; i++) fill[i] = new UnityEngine.Color(0.15f, 0.15f, 0.2f, 1f);
sheet.SetPixels(fill);
var sb = new System.Text.StringBuilder();
for (int f = 0; f < n; f++)
{
    var tex = frames[f].texture;
    var px = tex.GetPixels();
    int ox = f * (w + 2) * scale;
    float sumX = 0, sumY = 0, sumA = 0; int minX = w, maxX = -1, minY = h, maxY = -1;
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
    {
        var c = px[y * w + x];
        if (c.a > 0.1f) { sumX += x * c.a; sumY += y * c.a; sumA += c.a; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        var over = new UnityEngine.Color(0.15f + c.r * c.a, 0.15f + c.g * c.a, 0.2f + c.b * c.a, 1f);
        for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++) sheet.SetPixel(ox + x * scale + sx, y * scale + sy, over);
    }
    sb.Append("f" + f + ": bbox x[" + minX + ".." + maxX + "] y[" + minY + ".." + maxY + "] centroid(" + (sumA > 0 ? sumX / sumA : -1).ToString("F1") + "," + (sumA > 0 ? sumY / sumA : -1).ToString("F1") + ")\n");
}
sheet.Apply();
System.IO.File.WriteAllBytes(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0245\blast_frames.png", sheet.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(sheet);
return "canvas " + w + "x" + h + " ppu " + spec.pixelsPerUnit + " frames " + n + "\n" + sb;
