var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT) as UnityEngine.Component;
var cam = rigT.GetField("previewCamera", BFi).GetValue(rig) as Camera;
sb.Append("cam=").Append(cam.name).Append(" ortho=").Append(cam.orthographic).Append(" size=").Append(cam.orthographicSize)
  .Append(" pos=").Append(cam.transform.position).Append(" cull=").Append(cam.cullingMask).Append(" clear=").Append(cam.backgroundColor).Append("\n");
var live = (System.Collections.IDictionary)rigT.GetField("_live", BFi).GetValue(rig);
foreach (System.Collections.DictionaryEntry kv in live)
{
    sb.Append("LIVE key=").Append(kv.Key).Append(" val=").Append(kv.Value == null ? "<null>" : kv.Value.GetType().Name).Append("\n");
    var go = kv.Value as UnityEngine.GameObject;
    if (go == null) { var c = kv.Value as UnityEngine.Component; if (c != null) go = c.gameObject; if (go == null) { foreach (var f in kv.Value.GetType().GetFields(BFi)) { var g2 = f.GetValue(kv.Value) as UnityEngine.GameObject; if (g2 != null) { go = g2; break; } } } }
    if (go == null) { foreach (var f in kv.Value.GetType().GetFields(BFi)) sb.Append("   fld ").Append(f.Name).Append("=").Append(f.GetValue(kv.Value)).Append("\n"); continue; }
    sb.Append("  go=").Append(go.name).Append(" active=").Append(go.activeInHierarchy).Append(" layer=").Append(go.layer)
      .Append(" pos=").Append(go.transform.position).Append(" scale=").Append(go.transform.lossyScale).Append("\n");
    foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
        sb.Append("   rend ").Append(r.gameObject.name).Append(" type=").Append(r.GetType().Name).Append(" enabled=").Append(r.enabled)
          .Append(" activeGO=").Append(r.gameObject.activeInHierarchy).Append(" layer=").Append(r.gameObject.layer)
          .Append(" bounds=").Append(r.bounds).Append(" sprite=").Append(r is SpriteRenderer ? (((SpriteRenderer)r).sprite == null ? "<null>" : ((SpriteRenderer)r).sprite.name) : "-").Append("\n");
}
sb.Append("all renderers in scene: ");
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(FindObjectsSortMode.None)) sb.Append(r.gameObject.name).Append("/").Append(r.enabled).Append(" ");
return sb.ToString();
