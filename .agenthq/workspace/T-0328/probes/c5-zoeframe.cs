var sb = new System.Text.StringBuilder();
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "ZoePreviewWindow") win = w;
var go = win.GetType().GetField("_go", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(win) as UnityEngine.GameObject;
foreach (var p in go.GetComponentsInChildren<Laubrary.Launimator.ZonedAnimationPlayer>())
  sb.Append(p.gameObject.name).Append(" clip=").Append(p.CurrentClip).Append(" frame=").Append(p.CurrentFrame).Append(" playing=").Append(p.IsPlaying).Append("\n");
foreach (var e in ZAll(win.rootVisualElement))
  if (ZDrawn(e) && e is UnityEngine.UIElements.Label l && l.text.Contains("independently"))
    sb.Append("partsLine='").Append(l.text).Append("'\n");
return sb.ToString();
