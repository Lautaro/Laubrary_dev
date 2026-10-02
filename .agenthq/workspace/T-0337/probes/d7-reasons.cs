// T-0337 §2b — for every declared leaf dial of the form named by T337.form, ask the DRAWER ITSELF what it
// would do with it: does PyreFormShaperUI.DialInertReason return a sentence at the form's own defaults
// (i.e. is the dial GREYED WITH A REASON on screen), and does DialTooltip append a condition?
//
// No rendering, no mutation of any value — a pure read of the drawer's own three tables through the same
// FieldInfo + owner instance ZuiReflect hands it. Bounded by construction.
//
// Output: appends to out/dial-reasons.tsv  columns: form, path, declaringType.field, hasReason, hasCondition, reason
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var BFs = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var showT = ZType("ZUIShowIfAttribute");
var swarmOnlyT = ZType("PyreSwarmOnlyAttribute");
var rampI = ZType("IZuiRamp");

// the drawer is a private nested class of a public static class
System.Type drawerT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    System.Type outer = null;
    try { outer = a.GetType("Laubrary.PyreShaper.Editor.PyreFormShaperUI"); } catch { }
    if (outer == null) continue;
    foreach (var n in outer.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
        if (n.Name == "Drawer") drawerT = n;
    if (drawerT != null) break;
}
if (drawerT == null) return "DRAWER TYPE NOT FOUND";
var reasonM  = drawerT.GetMethod("DialInertReason", BFs);
var tooltipM = drawerT.GetMethod("DialTooltip", BFs);
var guardM   = drawerT.GetMethod("IsDialInertGuard", BFs);
if (reasonM == null || tooltipM == null) return "DialInertReason/DialTooltip NOT FOUND on Drawer";

System.Func<System.Type, System.Reflection.FieldInfo[]> fieldsOf = t => fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[];
System.Func<System.Type, bool> isLeaf = t =>
    t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
    || t == typeof(UnityEngine.Color) || t == typeof(UnityEngine.Color32)
    || t == typeof(UnityEngine.Vector2) || t == typeof(UnityEngine.Vector3) || t == typeof(UnityEngine.Vector4)
    || t == typeof(UnityEngine.Rect) || t == typeof(UnityEngine.AnimationCurve) || t == typeof(UnityEngine.Gradient)
    || t.Name == "ZUIValue" || t.Name == "ZuiGradient"
    || (rampI != null && rampI.IsAssignableFrom(t))
    || typeof(UnityEngine.Object).IsAssignableFrom(t);

string formName = UnityEditor.EditorPrefs.GetString("T337.form", "InfernoForm");
var ft = ZType(formName); if (ft == null) return formName + ": NO TYPE";
var form = System.Activator.CreateInstance(ft);

int leaves = 0, withReason = 0, withCondition = 0, guards = 0;

System.Action<object, string, int> walk = null;
walk = (owner, prefix, depth) =>
{
    if (owner == null || depth > 4) return;
    foreach (var f in fieldsOf(owner.GetType()))
    {
        string path = prefix + f.Name;
        if (swarmOnlyT != null && System.Attribute.IsDefined(f, swarmOnlyT)) continue;
        var t = f.FieldType;
        if (!isLeaf(t) && t.IsClass && !t.IsAbstract && !typeof(UnityEngine.Object).IsAssignableFrom(t)
            && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>)))
        {
            var child = f.GetValue(owner);
            if (child == null) { try { child = System.Activator.CreateInstance(t); f.SetValue(owner, child); } catch { } }
            walk(child, path + ".", depth + 1);
            continue;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
        {
            var lst = f.GetValue(owner) as System.Collections.IList;
            var et = t.GetGenericArguments()[0];
            if (lst != null && !isLeaf(et)) for (int i = 0; i < lst.Count && i < 2; i++) walk(lst[i], path + "[" + i + "].", depth + 1);
            continue;
        }
        leaves++;
        string key = (f.DeclaringType == null ? "?" : f.DeclaringType.Name) + "." + f.Name;
        string reason = reasonM.Invoke(null, new object[] { f, owner }) as string;
        string tip    = tooltipM.Invoke(null, new object[] { f }) as string;
        bool isGuard  = guardM != null && (bool)guardM.Invoke(null, new object[] { f });
        if (!string.IsNullOrEmpty(reason)) withReason++;
        if (!string.IsNullOrEmpty(tip)) withCondition++;
        if (isGuard) guards++;
        sb.Append(formName).Append('\t').Append(path).Append('\t').Append(key).Append('\t')
          .Append(string.IsNullOrEmpty(reason) ? "no" : "YES").Append('\t')
          .Append(string.IsNullOrEmpty(tip) ? "no" : "YES").Append('\t')
          .Append(isGuard ? "guard" : "").Append('\t')
          .Append((reason ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
    }
};
walk(form, "", 0);

string outp = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\dial-reasons.tsv";
if (!System.IO.File.Exists(outp))
    System.IO.File.WriteAllText(outp, "form\tpath\tkey\thasReason\thasCondition\tisGuard\treason\n");
System.IO.File.AppendAllText(outp, sb.ToString());
return formName + ": leaves=" + leaves + " greyedWithReasonAtDefaults=" + withReason
     + " conditionInTooltip=" + withCondition + " guardFields=" + guards;
