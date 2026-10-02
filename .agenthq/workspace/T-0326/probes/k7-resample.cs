var w = ZWin("ChunkWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var spec = af.GetValue(w);
System.Func<string,string> sprites = tag => {
    var f = spec.GetType().GetField("sprites");
    var l = f.GetValue(spec) as System.Collections.IList;
    return tag + " sprites=" + (l == null ? -1 : l.Count);
};
sb.Append(sprites("before")).Append("\n");
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Resample") btn = b; }
if (btn == null) return sb.Append("no Resample button drawn").ToString();
sb.Append("Resample rect=").Append(btn.worldBound).Append(" enabled=").Append(btn.enabledInHierarchy).Append("\n");
sb.Append("pressed -> ").Append(ZClick(btn)).Append("\n");
sb.Append(sprites("after")).Append("\n");
return sb.ToString();
