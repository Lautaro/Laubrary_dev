var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ if (b == null) { sb.Append("  !! MISSING\n"); return; }
  using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

sb.Append("bound before Browse = ").Append((curP.GetValue(win) as UnityEngine.Object) == null ? "<none>" : (curP.GetValue(win) as UnityEngine.Object).name).Append("\n");
Press(Btn("Browse"));

// how many documents exist, and how many thumbnails rendered?
var browseF = WT.GetField("_browse", BFi);
System.Reflection.FieldInfo thumbsF = null, thumbImgF = null;
for (var t = WT; t != null; t = t.BaseType)
{ if (thumbsF == null) thumbsF = t.GetField("_thumbs", BFi); if (thumbImgF == null) thumbImgF = t.GetField("_thumbImages", BFi); }
for (var t = WT; t != null; t = t.BaseType) if (browseF == null) browseF = t.GetField("_browse", BFi);
var browse = browseF == null ? null : browseF.GetValue(win) as System.Collections.ICollection;
var thumbs = thumbsF == null ? null : thumbsF.GetValue(win) as System.Collections.IDictionary;
var imgs = thumbImgF == null ? null : thumbImgF.GetValue(win) as System.Collections.ICollection;
sb.Append("documents found by Browse = ").Append(browse == null ? -1 : browse.Count).Append("\n");
sb.Append("thumbnails rendered       = ").Append(thumbs == null ? -1 : thumbs.Count).Append("\n");
sb.Append("thumbnail Images in tree  = ").Append(imgs == null ? -1 : imgs.Count).Append("\n");
if (browse != null) foreach (var o in browse)
{
    var u = o as UnityEngine.Object;
    bool has = thumbs != null && thumbs.Contains(o);
    sb.Append("  DOC '").Append(u == null ? "?" : u.name).Append("' thumbnail=").Append(has ? "YES" : "no").Append("\n");
}
// any Image with a real texture?
int withTex = 0, imgTotal = 0;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Image im) { imgTotal++; if (im.image != null) withTex++; }
sb.Append("Image elements=").Append(imgTotal).Append(" with a texture=").Append(withTex).Append("\n");
// the grid's own buttons
var grid = new System.Text.StringBuilder();
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b) grid.Append('[').Append(b.text).Append(']');
sb.Append("buttons in browse mode: ").Append(grid.ToString()).Append("\n");
return sb.ToString();
