// One temporal sample: the transport's own readout + the picture's lit pixels and hash.
var w = ZWin("ShaperWindow"); if (w == null) return "no ShaperWindow";
var sb = new System.Text.StringBuilder();
string readout = "?";
foreach (var e in ZAll(w.rootVisualElement))
{ var te = e as UnityEngine.UIElements.TextElement;
  if (te != null && ZDrawn(te) && te.text != null && te.text.StartsWith("frame ")) readout = te.text; }
UnityEngine.UIElements.Button play = null;
foreach (var e in ZAll(w.rootVisualElement))
{ var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text != null && (b.text.Contains("Play")||b.text.Contains("Pause"))) play = b; }
// the preview image the window is showing
Texture2D tex = null;
foreach (var e in ZAll(w.rootVisualElement))
{
    var img = e as UnityEngine.UIElements.Image;
    if (img != null && img.image is Texture2D t2 && ZDrawn(img)) { tex = t2; break; }
    var bg = e.resolvedStyle.backgroundImage.texture;
    if (bg != null && ZDrawn(e) && e.worldBound.width > 100 && tex == null) tex = bg;
}
int lit = -1; uint h = 0;
if (tex != null)
{
    // A preview texture is not CPU-readable, so blit it through a RenderTexture and read that back.
    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
    var prevA = RenderTexture.active;
    Graphics.Blit(tex, rt); RenderTexture.active = rt;
    var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
    copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); copy.Apply();
    RenderTexture.active = prevA; RenderTexture.ReleaseTemporary(rt);
    var px = copy.GetPixels32(); lit = 0; h = 2166136261;
    foreach (var p in px) { if (p.a > 8) lit++; h = (h ^ p.r) * 16777619; h = (h ^ p.g) * 16777619; h = (h ^ p.b) * 16777619; h = (h ^ p.a) * 16777619; }
    UnityEngine.Object.DestroyImmediate(copy);
}
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T324.t0",0)).ToString("F2"))
  .Append("  readout='").Append(readout).Append("'  btn='").Append(play != null ? play.text : "?").Append("'")
  .Append("  tex=").Append(tex != null ? tex.width + "x" + tex.height : "none")
  .Append(" lit=").Append(lit).Append(" hash=").Append(h.ToString("X8"));
return sb.ToString();
