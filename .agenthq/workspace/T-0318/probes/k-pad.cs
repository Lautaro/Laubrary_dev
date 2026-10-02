// T-0318 — ZuiPad / ZuiValue2DControl arrow nudge, one control, with Undo checked through the DOCUMENT.
string want = UnityEditor.EditorPrefs.GetString("T318.ctl", "ZuiPad");
var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/AuditT0318/NudgeDoc.asset");
UnityEngine.UIElements.VisualElement ctl = null;
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == want && ZDrawn(e) && e.enabledInHierarchy) { ctl = e; break; }
if (ctl == null) return want + " not drawn";
var t = ctl.GetType();
System.Func<string> snap = () => {
    if (t.Name == "ZuiPad") return "Value=" + t.GetProperty("Value", BFa).GetValue(ctl);
    foreach (var f in t.GetFields(BFa)) { var v = f.GetValue(ctl); if (v == null) continue;
        var p = v.GetType().GetProperty("Static", BFa); if (p != null) return f.Name + ".Static=" + p.GetValue(v); }
    return "<none>";
};
var sb = new System.Text.StringBuilder();
sb.Append(want).Append(" '").Append(ZCaption(ctl)).Append("' focusable=").Append(ctl.focusable).Append("\n");
ctl.Focus();
sb.Append("  focused=").Append(win.rootVisualElement.focusController.focusedElement == ctl)
  .Append(" docDirtyBefore=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
string before = snap();
int g0 = UnityEditor.Undo.GetCurrentGroup();
using (var ev = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None))
{ ev.target = ctl; ctl.SendEvent(ev); }
string after = snap();
int g1 = UnityEditor.Undo.GetCurrentGroup();
sb.Append("  before ").Append(before).Append("\n  after  ").Append(after)
  .Append("\n  CHANGED=").Append(before != after).Append(" undoGroupsOpened=").Append(g1 - g0)
  .Append(" lastGroupName='").Append(UnityEditor.Undo.GetCurrentGroupName()).Append("'\n");
UnityEditor.Undo.PerformUndo();
UnityEditor.Undo.FlushUndoRecordObjects();
string undone = snap();
sb.Append("  afterOneUndo ").Append(undone).Append("  RESTORED=").Append(undone == before).Append("\n");
return sb.ToString();
