var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT) as UnityEngine.Component;
if (rig == null) return "no rig";
var live = (System.Collections.IDictionary)rigT.GetField("_live", BFi).GetValue(rig);
foreach (System.Collections.DictionaryEntry kv in live)
{
    var go = kv.Value as UnityEngine.GameObject;
    sb.Append("live go=").Append(go == null ? "<null>" : go.name).Append(" active=").Append(go != null && go.activeInHierarchy).Append("\n");
    if (go == null) continue;
    foreach (var c in go.GetComponentsInChildren<UnityEngine.Component>(true))
        sb.Append("   ").Append(c.GetType().Name).Append(" on ").Append(c.gameObject.name).Append("\n");
    // tick anything that has its own Update, several times, the way a foreground editor would
    for (int i = 0; i < 5; i++)
        foreach (var mb in go.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true))
        {
            var up = mb.GetType().GetMethod("Update", BFi | System.Reflection.BindingFlags.DeclaredOnly);
            if (up != null && up.GetParameters().Length == 0) { try { up.Invoke(mb, null); } catch { } }
        }
    sb.Append("   after ticks: children=").Append(go.transform.childCount)
      .Append(" renderers=").Append(go.GetComponentsInChildren<UnityEngine.Renderer>(true).Length).Append("\n");
}
return sb.ToString();
