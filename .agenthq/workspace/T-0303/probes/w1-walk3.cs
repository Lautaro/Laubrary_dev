var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };

// stop playback through its own button
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause")))
{ sb.Append("transport reads '").Append(b.text).Append("' while playing\n"); using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } break; }
sb.Append("playing after press=").Append(WT.GetField("playing", BFi).GetValue(win)).Append("\n");
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause")))
{ sb.Append("transport now reads '").Append(b.text).Append("'\n"); break; }

System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;

// NEVER BLANK: every frame of the loop must paint something
int blank = 0; var lits = new System.Text.StringBuilder();
var hashes = new System.Collections.Generic.HashSet<int>();
for (int i = 0; i < doc.frameCount; i++)
{
    var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, i);
    int lit = 0; int h = 17;
    foreach (var c in px) { if (c.a > 0) lit++; h = h * 31 + c.r + c.g * 7 + c.b * 13 + c.a * 3; }
    hashes.Add(h);
    if (lit == 0) blank++;
    lits.Append(lit).Append(' ');
}
sb.Append("frames=").Append(doc.frameCount).Append(" blankFrames=").Append(blank).Append(" distinctHashes=").Append(hashes.Count).Append("\n");
sb.Append("lit per frame: ").Append(lits.ToString()).Append("\n");
sb.Append("doc dirty after play=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
return sb.ToString();
