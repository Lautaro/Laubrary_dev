// Tick the stage the way a focused editor does (ExecuteAlways Update does not run while the editor is in
// the background — §1), then render the rig's own preview camera.
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
int ticked = 0;
for (int pass = 0; pass < 3; pass++)
    foreach (var mb in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(FindObjectsSortMode.None))
    {
        var t = mb.GetType();
        if (!t.Namespace?.StartsWith("Laubrary") ?? true) continue;
        var up = t.GetMethod("Update", BFi | System.Reflection.BindingFlags.DeclaredOnly);
        if (up == null || up.GetParameters().Length != 0) continue;
        try { up.Invoke(mb, null); ticked++; } catch { }
    }
sb.Append("ticked=").Append(ticked).Append("\n");
foreach (var mb in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(FindObjectsSortMode.None))
    if (mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("Laubrary"))
        sb.Append("  scene: ").Append(mb.GetType().Name).Append(" on ").Append(mb.gameObject.name).Append("\n");
int rends = 0;
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(FindObjectsSortMode.None)) rends++;
sb.Append("renderers=").Append(rends).Append("\n");
return sb.ToString();
