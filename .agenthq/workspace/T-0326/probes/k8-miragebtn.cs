// Press Chunks' own "Preview in Mirage" — but ONLY when no scene is dirty, because
// EnsureMirageStageOpen offers to save and a modal prompt would wedge the editor.
var sb = new System.Text.StringBuilder();
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
if (sc.isDirty)
{
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(sc.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
    sb.Append("reloaded from disk (nothing saved); dirty=")
      .Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty).Append("\n");
    return sb.Append("(window rebuilt — run again to press)").ToString();
}
var w = ZWin("ChunkWindow"); if (w == null) return sb.Append("no window").ToString();
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Preview in Mirage") btn = b; }
if (btn == null) return sb.Append("no Preview in Mirage button drawn").ToString();
UnityEngine.UIElements.ScrollView sv = null;
for (var p = btn.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null && (btn.worldBound.yMax > w.position.height || btn.worldBound.y < 0)) { sv.ScrollTo(btn); return sb.Append("scrolled it into view — run again to press").ToString(); }
sb.Append("btn=").Append(btn.worldBound).Append(" pressed=").Append(ZClick(btn)).Append("\n");
sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("after scene=").Append(sc.path).Append("\n");
var rigT = ZType("MirageRig");
sb.Append("rig=").Append(rigT == null ? "?" : (UnityEngine.Object.FindFirstObjectByType(rigT) == null ? "none" : "yes")).Append("\n");
return sb.ToString();
