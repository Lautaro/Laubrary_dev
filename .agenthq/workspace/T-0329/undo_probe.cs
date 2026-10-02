// Behavioural probe: fire the REAL callbacks the buttons are wired to, then Ctrl+Z, and check the data came back.
var sb = new System.Text.StringBuilder();
var wt = System.Type.GetType("Laubrary.Lazor.Editor.LazorWindow, com.Lautaro-Arino.Laubrary.Lazor.Editor");
foreach (var old in UnityEngine.Resources.FindObjectsOfTypeAll(wt))
    if (old is UnityEditor.EditorWindow e2 && e2.titleContent.text.Contains("LazorWalk")) e2.Close();

var w = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(wt);
w.titleContent = new UnityEngine.GUIContent("LazorWalk");
w.ShowUtility();
w.position = new UnityEngine.Rect(80, 80, 1180, 740);
var so = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>("Assets/Lazor/New Lazor Shape.asset");
wt.GetMethod("SetAsset", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)
  .Invoke(w, new object[]{ so });
w.Repaint();

var layersField = so.GetType().GetField("layers");
System.Func<int> count = () => ((System.Collections.IList)layersField.GetValue(so)).Count;

System.Func<string, UnityEngine.UIElements.Button> find = label => {
  UnityEngine.UIElements.Button hit = null;
  System.Action<UnityEngine.UIElements.VisualElement> rec = null;
  rec = ve => { if (hit != null) return;
    if (ve is UnityEngine.UIElements.Button b && b.text == label) { hit = b; return; }
    foreach (var c in ve.Children()) rec(c); };
  rec(w.rootVisualElement); return hit;
};

// pull the Action the Button's Clickable was constructed with
System.Func<UnityEngine.UIElements.Button, System.Action> handlerOf = btn => {
  var cl = btn.clickable;
  foreach (var f in typeof(UnityEngine.UIElements.Clickable).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic))
    if (typeof(System.Action).IsAssignableFrom(f.FieldType)) { var a = f.GetValue(cl) as System.Action; if (a != null) return a; }
  return null;
};

int before = count();
sb.AppendLine("layers before          = " + before);

var add = find("+ Add");
var h = add != null ? handlerOf(add) : null;
if (h == null) return sb.AppendLine("could not reach the '+ Add' handler").ToString();
h();
int after = count();
sb.AppendLine("layers after '+ Add'   = " + after + (after == before + 1 ? "   OK" : "   FAILED"));

UnityEditor.Undo.PerformUndo();
int undone = count();
sb.AppendLine("layers after UNDO      = " + undone + (undone == before ? "   OK - structural undo restores" : "   FAILED - undo did not restore"));

// value edit: drive the real Thickness slider callback, then undo
var lay0 = ((System.Collections.IList)layersField.GetValue(so))[0];
var thickF = lay0.GetType().GetField("thickness");
float t0 = (float)thickF.GetValue(lay0);
UnityEngine.UIElements.Slider slider = null;
System.Action<UnityEngine.UIElements.VisualElement> rec2 = null;
rec2 = ve => { if (slider == null) { if (ve is UnityEngine.UIElements.Slider s && s.tooltip.Contains("Stroke width")) slider = s; foreach (var c in ve.Children()) rec2(c); } };
rec2(w.rootVisualElement);
if (slider == null) sb.AppendLine("Thickness slider NOT FOUND");
else {
  slider.value = 0.1234f;                       // fires the registered callback, exactly as a drag would
  float t1 = (float)thickF.GetValue(((System.Collections.IList)layersField.GetValue(so))[0]);
  sb.AppendLine("thickness " + t0 + " -> " + t1 + (System.Math.Abs(t1 - 0.1234f) < 0.0005f ? "   OK - slider writes through" : "   FAILED"));
  UnityEditor.Undo.PerformUndo();
  float t2 = (float)thickF.GetValue(((System.Collections.IList)layersField.GetValue(so))[0]);
  sb.AppendLine("thickness after UNDO   = " + t2 + (System.Math.Abs(t2 - t0) < 0.0005f ? "   OK - value undo restores" : "   FAILED - undo did not restore"));
}
sb.AppendLine("console errors during probe: see get_console_logs");
return sb.ToString();
