var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();

// the EXACT trap shape T-0293 hit: a wrap:true MiniRadio, wrapped in Z.Field, inside a Z.HGroup, in a 350px box
var radio = Laubrary.Zui.Z.MiniRadio(0, new string[] { "Opacity", "Distance from edge", "Brightness", "Height" },
    "probe", i => { }, true);
var field = Laubrary.Zui.Z.Field("Quantity", "probe", radio);
var hg = Laubrary.Zui.Z.HGroup(field);
var box = Laubrary.Zui.Z.Box("A24 probe", "probe", hg);
box.style.width = 350f;
var host = new UnityEngine.UIElements.VisualElement();
host.style.width = 360f;
host.style.position = UnityEngine.UIElements.Position.Absolute;
host.style.left = 0f; host.style.top = 0f;
host.Add(box);
win.rootVisualElement.Add(host);
UnityEditor.SessionState.SetString("A24.t0297host", "added");
sb.Append("probe shape added; field classes: ");
foreach (var c in field.GetClasses()) sb.Append(c).Append(' ');
sb.Append("\nradio classes: ");
foreach (var c in radio.GetClasses()) sb.Append(c).Append(' ');
sb.Append("\nfield has zui-field--wrap = ").Append(field.ClassListContains("zui-field--wrap")).Append("\n");
return sb.ToString();
