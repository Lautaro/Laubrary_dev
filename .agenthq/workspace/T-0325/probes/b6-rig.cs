var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT);
if (rig == null) return "no rig";
var vf = rigT.GetField("view", BFi);
sb.Append("rig.view=").Append(vf.GetValue(rig) == null ? "<null>" : vf.GetValue(rig).ToString()).Append("\n");
var liveF = rigT.GetField("_live", BFi);
sb.Append("live=").Append(((System.Collections.IDictionary)liveF.GetValue(rig)).Count).Append("\n");
sb.Append("execAlways=").Append(System.Attribute.IsDefined(rigT, typeof(UnityEngine.ExecuteAlways))).Append(" execEdit=").Append(System.Attribute.IsDefined(rigT, typeof(UnityEngine.ExecuteInEditMode))).Append("\n");
var view = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Mirage/AuditT325View.asset", ZType("MirageView"));
var swap = rigT.GetMethod("SwapTo", BFi);
if (swap != null) { swap.Invoke(rig, new object[]{ view }); sb.Append("SwapTo called\n"); }
else { vf.SetValue(rig, view); var rz = rigT.GetMethod("Realize", BFi); if (rz != null) rz.Invoke(rig, null); sb.Append("assigned+Realize\n"); }
sb.Append("live now=").Append(((System.Collections.IDictionary)liveF.GetValue(rig)).Count).Append("\n");
var up = rigT.GetMethod("Update", BFi); if (up != null) { up.Invoke(rig, null); sb.Append("Update ticked; live=").Append(((System.Collections.IDictionary)liveF.GetValue(rig)).Count).Append("\n"); }
return sb.ToString();
