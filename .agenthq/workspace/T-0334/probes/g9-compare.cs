// Compare a baked Shaper sheet to the live renderer, frame for frame.
var sb = new System.Text.StringBuilder();
string docPath = UnityEditor.EditorPrefs.GetString("T334.cmpDoc", "Assets/Demos/ShaperDemo/AuditT334ShaperA.asset");
string pngPath = UnityEditor.EditorPrefs.GetString("T334.cmpPng", "Assets/Demos/ShaperDemo/AuditT334ShaperA.png");
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(docPath);
if (doc == null) return "no doc " + docPath;
byte[] bytes = System.IO.File.ReadAllBytes(pngPath);
var tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
tex.LoadImage(bytes);
int w = doc.canvasWidth, h = doc.canvasHeight;
sb.Append("doc ").Append(w).Append("x").Append(h).Append(" frames=").Append(doc.frameCount)
  .Append(" sheet=").Append(tex.width).Append("x").Append(tex.height).Append("\n");
int cols = tex.width / w, rows = tex.height / h;
sb.Append("cols=").Append(cols).Append(" rows=").Append(rows).Append("\n");
var sheetPx = tex.GetPixels32();
var framePx = new UnityEngine.Color32[w * h];
var pool = new Laubrary.Shaper.ShaperRenderBufferPool();
int totalDiff = 0, worstFrame = -1, worst = 0;
for (int f = 0; f < doc.frameCount; f++)
{
    Laubrary.Shaper.ShaperDocumentRenderer.RenderPhase(doc, doc.PhaseOfFrame(f), framePx, null, f, pool, null);
    int cx = f % cols, cy = f / cols;
    int ox = cx * w, oy = (rows - 1 - cy) * h;
    int diff = 0;
    for (int y = 0; y < h; y++)
      for (int x = 0; x < w; x++)
      {
          var a = framePx[y * w + x];
          var b = sheetPx[(oy + y) * tex.width + ox + x];
          if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) diff++;
      }
    totalDiff += diff;
    if (diff > worst) { worst = diff; worstFrame = f; }
    sb.Append("  frame ").Append(f).Append(": differing pixels=").Append(diff).Append(" of ").Append(w * h).Append("\n");
}
UnityEngine.Object.DestroyImmediate(tex);
sb.Append("TOTAL differing=").Append(totalDiff).Append(" worstFrame=").Append(worstFrame).Append(" (").Append(worst).Append(")\n");
return sb.ToString();
