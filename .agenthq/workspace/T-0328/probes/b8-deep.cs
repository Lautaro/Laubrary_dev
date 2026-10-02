var sb = new System.Text.StringBuilder();
sb.Append("RP=").Append(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "<builtin>" : UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name).Append("\n");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT) as UnityEngine.Component;
var live = (System.Collections.IDictionary)rigT.GetField("_live", BFi).GetValue(rig);
foreach (System.Collections.DictionaryEntry kv in live)
{
    var go = kv.Value as UnityEngine.GameObject;
    sb.Append("go=").Append(go.name).Append(" children=").Append(go.transform.childCount).Append("\n");
    foreach (var c in go.GetComponents<UnityEngine.Component>()) sb.Append("  comp ").Append(c.GetType().Name).Append("\n");
    foreach (UnityEngine.Transform t in go.transform)
    {
        sb.Append("  child ").Append(t.name).Append(" active=").Append(t.gameObject.activeInHierarchy).Append(" kids=").Append(t.childCount).Append("\n");
        foreach (var c in t.GetComponents<UnityEngine.Component>()) sb.Append("     comp ").Append(c.GetType().Name).Append("\n");
    }
}
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(FindObjectsSortMode.None))
    sb.Append("scene rend ").Append(r.gameObject.name).Append(" bounds=").Append(r.bounds).Append(" enabled=").Append(r.enabled).Append(" activeGO=").Append(r.gameObject.activeInHierarchy).Append("\n");
return sb.ToString();
