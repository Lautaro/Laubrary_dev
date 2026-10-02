var sb = new System.Text.StringBuilder();
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "ZoePreviewWindow") win = w;
UnityEngine.UIElements.DropdownField dd = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEngine.UIElements.DropdownField d && ZDrawn(e)) dd = d;
sb.Append("before: ").Append(win.GetType().GetField("_clip", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(win)).Append("\n");
dd.value = "LegsWalk_N";   // goes through the control's own change callback
sb.Append("after pick: _clip='").Append(win.GetType().GetField("_clip", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(win)).Append("'\n");
var go = win.GetType().GetField("_go", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(win) as UnityEngine.GameObject;
foreach (var p in go.GetComponentsInChildren<Laubrary.Launimator.ZonedAnimationPlayer>())
  sb.Append("  ").Append(p.gameObject.name).Append(" clip=").Append(p.CurrentClip).Append(" frame=").Append(p.CurrentFrame).Append(" playing=").Append(p.IsPlaying).Append("\n");
// drive the window's own Tick through EditorApplication.update, then sample again
int n = 0; UnityEditor.EditorApplication.CallbackFunction cb = null;
cb = () => { n++; if (n >= 200) UnityEditor.EditorApplication.update -= cb; };
UnityEditor.EditorApplication.update += cb;
return sb.ToString();
