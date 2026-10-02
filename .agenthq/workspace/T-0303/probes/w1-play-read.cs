var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("SAMPLES: ").Append(UnityEditor.SessionState.GetString("A25.samples", "<none>")).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
sb.Append("playing now=").Append(win.GetType().GetField("playing", BFi).GetValue(win))
  .Append(" frame now=").Append(win.GetType().GetField("currentFrame", BFi).GetValue(win)).Append("\n");
return sb.ToString();
