var sb = new System.Text.StringBuilder();
var spec = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>("Assets/Pyre/AuditT328PyreA.asset");
if (spec == null) return "no spec";
string png = "Assets/Pyre/AuditT328PyreA.png";
var bytes = System.IO.File.ReadAllBytes(png);
var baked = new UnityEngine.Texture2D(2,2, UnityEngine.TextureFormat.RGBA32, false); baked.LoadImage(bytes);
int cols, rows;
var live = Laubrary.Pyre.PyreRenderer.RenderSheet(spec, out cols, out rows, 8);
sb.Append("spec ").Append(spec.Width).Append("x").Append(spec.Height).Append(" frames=").Append(spec.frameCount)
  .Append(" liveSheet=").Append(live.width).Append("x").Append(live.height)
  .Append(" bakedSheet=").Append(baked.width).Append("x").Append(baked.height)
  .Append(" cols=").Append(cols).Append(" rows=").Append(rows).Append("\n");
if (live.width != baked.width || live.height != baked.height) { UnityEngine.Object.DestroyImmediate(live); UnityEngine.Object.DestroyImmediate(baked); return sb.Append("SIZE MISMATCH\n").ToString(); }
var a = live.GetPixels32(); var b = baked.GetPixels32();
int cw = spec.Width, ch = spec.Height;
int total = 0;
for (int f = 0; f < spec.frameCount; f++)
{
    var r = Laubrary.Pyre.PyreRenderer.FrameRect(f, cols, rows, cw, ch);
    int ox = (int)r.x, oy = (int)r.y; int diff = 0;
    for (int y = 0; y < ch; y++)
      for (int x = 0; x < cw; x++)
      {
          int i = (oy + y) * live.width + ox + x;
          if (!a[i].Equals(b[i])) diff++;
      }
    total += diff;
    sb.Append("  frame ").Append(f).Append(": differing=").Append(diff).Append(" of ").Append(cw*ch).Append("\n");
}
UnityEngine.Object.DestroyImmediate(live); UnityEngine.Object.DestroyImmediate(baked);
sb.Append("TOTAL differing=").Append(total).Append("\n");
return sb.ToString();
