var t = ZType("ChunkWindow"); if (t == null) return "no ChunkWindow type";
var win = ZWin("ChunkWindow");
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Chunks"); win = ZWin("ChunkWindow"); }
if (win == null) return "no chunk window (menu?)";
win.position = new UnityEngine.Rect(0, 20, 820, 900);
win.titleContent = new GUIContent("T320Chunks");
win.Show(); win.Focus(); win.Repaint();
var guids = UnityEditor.AssetDatabase.FindAssets("t:ChunkSpec");
var sb = new System.Text.StringBuilder();
foreach (var g in guids) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return "chunks open " + win.position + " specs: " + sb;
