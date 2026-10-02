// Bake the bound document to an explicit scratch folder, then compare the baked sheet frame-for-frame
// against the live renderer (ShaperBaker.RenderFrame, the same call the preview cache makes).
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(w);
string folder = UnityEditor.EditorPrefs.GetString("T337.bakeFolder", "Assets/Shaper/AuditT337Bake");
if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", folder.Substring(folder.LastIndexOf('/')+1));
var bakerT = ZType("ShaperBaker");
var bake = bakerT.GetMethod("Bake", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
var res = bake.Invoke(null, new object[]{ doc, folder, null, 16f, true, true });
var sb = new System.Text.StringBuilder();
var rt = res.GetType();
foreach (var f in rt.GetFields()) sb.Append(f.Name).Append("=").Append(f.GetValue(res)).Append("\n");
string sheetPath = (string)rt.GetField("sheetPath").GetValue(res);
int frames = (int)rt.GetField("sheetFrames").GetValue(res);
int cols = (int)rt.GetField("columns").GetValue(res);
// the imported sheet is non-readable; decode the PNG bytes into a scratch readable texture instead
var bytes = System.IO.File.ReadAllBytes(sheetPath);
var tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
if (!UnityEngine.ImageConversion.LoadImage(tex, bytes, false)) return sb.Append("could not decode ").Append(sheetPath).ToString();
sb.Append("sheet ").Append(tex.width).Append("x").Append(tex.height).Append("\n");
var render = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
var order = bakerT.GetMethod("PlaybackOrder", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Invoke(null, new object[]{doc}) as System.Collections.IList;
int cw = (int)doc.GetType().GetField("canvasWidth", BFi).GetValue(doc);
int ch = (int)doc.GetType().GetField("canvasHeight", BFi).GetValue(doc);
var sheetPx = tex.GetPixels32();
int diff = 0, compared = 0, blanks = 0;
for (int i = 0; i < order.Count; i++) {
  int src = (int)order[i];
  if (src < 0) { blanks++; continue; }
  var live = render.Invoke(null, new object[]{ doc, src }) as UnityEngine.Color32[];
  int col = i % cols, row = i / cols;
  // the baked sheet's rows run top-down; Texture2D pixel (0,0) is bottom-left
  int x0 = col * cw, y0 = tex.height - (row + 1) * ch;
  for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) {
    var a = sheetPx[(y0 + y) * tex.width + (x0 + x)];
    var b = live[y * cw + x];
    compared++;
    if (a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a) diff++;
  }
}
sb.Append("frames=").Append(frames).Append(" blanks=").Append(blanks).Append(" comparedPixels=").Append(compared).Append(" DIFFERING=").Append(diff).Append("\n");
return sb.ToString();
