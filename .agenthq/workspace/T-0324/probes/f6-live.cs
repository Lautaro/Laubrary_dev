var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT) as MonoBehaviour;
if (rig == null) return "no rig";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var live = rigT.GetField("_live", BFi).GetValue(rig) as System.Collections.IDictionary;
var sb = new System.Text.StringBuilder();
var cam = rigT.GetField("previewCamera", BFi).GetValue(rig) as Camera;
sb.AppendLine("cam pos=" + cam.transform.position + " ortho=" + cam.orthographicSize + " aspect=" + cam.aspect);
foreach (System.Collections.DictionaryEntry kv in live)
{
    var go = kv.Value as GameObject;
    if (go == null) { sb.AppendLine(kv.Key + " -> null"); continue; }
    var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
    sb.AppendLine(kv.Key + " -> " + go.name + " pos=" + go.transform.position + " scale=" + go.transform.localScale
      + " active=" + go.activeInHierarchy + " sprites=" + srs.Length);
    foreach (var sr in srs) sb.AppendLine("      SR " + sr.name + " sprite=" + (sr.sprite != null ? sr.sprite.name : "NONE")
      + " enabled=" + sr.enabled + " visible=" + sr.isVisible + " bounds=" + sr.bounds);
}
return sb.ToString();
