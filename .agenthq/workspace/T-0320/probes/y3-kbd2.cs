// Keyboard-only operation of the Layers card, take 2. Buttons are activated with the navigation SUBMIT
// that Enter/Space raise (a raw KeyDownEvent is not how UITK activates a Button); value controls get Right.
// "Changed" is measured on what the user can see: the control's own on/off class, its caption, and the
// preview stage's pixels.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var secT = ZType("ZuiSection");
UnityEngine.UIElements.VisualElement card = null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-section__title") && l.text == "Layers") { card = e; break; } } if (card != null) break; }
if (card == null) return "no Layers section";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null; for (var t=win.GetType(); t!=null && stageF==null; t=t.BaseType) stageF=t.GetField("stage",BFi);
var stage = stageF.GetValue(win);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
System.Func<string> pix = () => { refresh.Invoke(stage, null); var tex = texF.GetValue(stage) as UnityEngine.Texture2D; if (tex == null) return "notex";
  var px = tex.GetPixels32(); unchecked { uint h = 2166136261u; foreach (var p in px) { h=(h^p.r)*16777619u; h=(h^p.g)*16777619u; h=(h^p.b)*16777619u; h=(h^p.a)*16777619u; } return h.ToString("X8"); } };
var res = new System.Text.StringBuilder(); int total=0, worked=0;
var targets = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
foreach (var e in ZAll(card)) if (ZDrawn(e) && ZIsLeafCtrl(e)) targets.Add(e);
foreach (var e in targets) {
  string cap = ZCaption(e);
  if (cap != null && (cap.Contains("×") || cap.Contains("Dup") || cap == "+ Add layer")) { res.AppendLine("SKIPPED (adds/removes a layer) '" + cap + "'"); continue; }
  total++;
  string sig0 = ZCls(e) + "|" + cap + "|" + pix();
  e.Focus();
  bool isButton = e is UnityEngine.UIElements.Button;
  for (int i=0;i<1;i++) {
    if (isButton) { var ns = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled(); ns.target = e; using (ns) e.SendEvent(ns); }
    else { var kd = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None); kd.target = e; using (kd) e.SendEvent(kd); }
  }
  string sig1 = ZCls(e) + "|" + ZCaption(e) + "|" + pix();
  bool ch = sig0 != sig1; if (ch) worked++;
  if (isButton && ch) { var ns2 = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled(); ns2.target = e; using (ns2) e.SendEvent(ns2); }
  res.AppendLine((ch ? "OPERATED " : "DEAD     ") + "'" + cap + "' " + e.GetType().Name + (isButton ? " (submit)" : " (Right)"));
}
return "Layers card, keyboard only: " + worked + " of " + total + " controls responded\n" + res;
