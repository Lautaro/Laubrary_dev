var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Stop") { ZClick(b); sb.Append("stopped\n"); } }
var gs = UnityEditor.AssetDatabase.FindAssets("t:Sprite", new string[]{"Assets/Demos/ProtoGuyDemo"});
string path=null; UnityEngine.Sprite sp=null;
foreach (var g in gs) { var p=UnityEditor.AssetDatabase.GUIDToAssetPath(g); var s=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(p); if (s!=null) { path=p; sp=s; break; } }
sb.Append("sprite=").Append(path).Append(" ").Append(sp!=null?sp.name:"null").Append("\n");
// set through the window's own ObjectField, the way the picker would
foreach (var e in ZAll(w.rootVisualElement)) { var of=e as UnityEditor.UIElements.ObjectField; if (of!=null && ZDrawn(of) && of.objectType==typeof(UnityEngine.Sprite)) { of.value=sp; sb.Append("assigned via ObjectField\n"); break; } }
return sb.ToString();
