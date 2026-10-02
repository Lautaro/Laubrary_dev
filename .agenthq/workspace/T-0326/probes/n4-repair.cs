// My section sweep pressed two segmented controls that were NOT section toggles (a light's Point /
// Directional kind), which is a data edit on the SHIPPED demo document. Undo them and confirm the asset
// matches disk again.
var sb = new System.Text.StringBuilder();
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.Append("dirty before=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
sb.Append("json before=").Append(UnityEditor.EditorJsonUtility.ToJson(doc).GetHashCode().ToString("X8")).Append("\n");
for (int i = 0; i < 6; i++) UnityEditor.Undo.PerformUndo();
sb.Append("after 6 undos dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
sb.Append("json after=").Append(UnityEditor.EditorJsonUtility.ToJson(doc).GetHashCode().ToString("X8")).Append("\n");
return sb.ToString();
