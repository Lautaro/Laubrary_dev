var w = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
System.Reflection.MethodInfo sa=null, rb=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sa.Invoke(w, new object[]{ doc }); if (rb!=null) rb.Invoke(w,null);
w.position = new Rect(20,20,900,880);
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=0;Canvas=0;Layers=1;Shape=1;Fill=1;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
if (rb!=null) rb.Invoke(w,null);
w.Repaint();
sb.Append("bound=").Append(UnityEditor.AssetDatabase.GetAssetPath(doc)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
// find the Play button and press it
UnityEngine.UIElements.Button play=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && b.text.Contains("Play")) play=b; }
sb.Append("play=").Append(play!=null?play.text:"NONE").Append("\n");
if (play!=null) ZClick(play);
sb.Append("t0=").Append(UnityEditor.EditorApplication.timeSinceStartup.ToString("F2")).Append("\n");
return sb.ToString();
