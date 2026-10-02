var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
return "SAMPLES: " + UnityEditor.SessionState.GetString("T0307.play", "<none>")
     + " | playing=" + win.GetType().GetField("playing", BFi).GetValue(win);
