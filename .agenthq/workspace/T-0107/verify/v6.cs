var sb = new System.Text.StringBuilder();
var single = new System.Reflection.Emit.OpCode[256];
var multi  = new System.Reflection.Emit.OpCode[256];
foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)) {
    var oc = (System.Reflection.Emit.OpCode)f.GetValue(null);
    if (oc.Size == 1) single[oc.Value & 0xFF] = oc; else multi[oc.Value & 0xFF] = oc; }
System.Func<System.Reflection.Emit.OperandType,int> osz = t => {
    switch (t) {
      case System.Reflection.Emit.OperandType.InlineNone: return 0;
      case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
      case System.Reflection.Emit.OperandType.ShortInlineI:
      case System.Reflection.Emit.OperandType.ShortInlineVar: return 1;
      case System.Reflection.Emit.OperandType.InlineVar: return 2;
      case System.Reflection.Emit.OperandType.InlineI8:
      case System.Reflection.Emit.OperandType.InlineR: return 8;
      default: return 4; } };

var seen = new System.Collections.Generic.HashSet<string>();
var queue = new System.Collections.Generic.Queue<System.Reflection.MethodBase>();
var allocSites = new System.Collections.Generic.List<string>();
var externalTypes = new System.Collections.Generic.SortedSet<string>();
var unresolved = new System.Collections.Generic.SortedSet<string>();
var listed = new System.Collections.Generic.HashSet<string>(new string[]{
  "ShaperFillResolver","ShaperFillOps","ShaperEvaluator","ShaperOps","ShaperSdf","ShaperField","ShaperBorder","ShaperFillBuffers"});
var excludedByAudit = new System.Collections.Generic.HashSet<string>(new string[]{
  "Resolve","Walk","Refuse","BindBorder","Bind","UnavailableReason","Published",
  "HasContributingMember","FillKindName","CompileStrip","JoinOp"});

var root = typeof(Laubrary.Shaper.ShaperFillResolver).GetMethod("PaintTile");
queue.Enqueue(root);
int visited = 0;
while (queue.Count > 0 && visited < 4000) {
    var m = queue.Dequeue();
    string key = (m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "::" + m.Name + "::" + m.MetadataToken;
    if (!seen.Add(key)) continue;
    visited++;
    string dt = m.DeclaringType != null ? m.DeclaringType.Name : "?";
    string ns = m.DeclaringType != null ? (m.DeclaringType.Namespace ?? "") : "";
    byte[] il = null;
    try { var b = m.GetMethodBody(); il = b != null ? b.GetILAsByteArray() : null; } catch {}
    if (il == null) continue;
    System.Type[] ta = null, ma = null;
    try { if (m.DeclaringType != null && m.DeclaringType.IsGenericType) ta = m.DeclaringType.GetGenericArguments();
          if (m.IsGenericMethod) ma = m.GetGenericArguments(); } catch {}
    int i = 0;
    while (i < il.Length) {
        System.Reflection.Emit.OpCode op;
        if (il[i]==0xFE && i+1<il.Length) { op = multi[il[i+1]]; i+=2; } else { op = single[il[i]]; i+=1; }
        if (op.OperandType == System.Reflection.Emit.OperandType.InlineSwitch) {
            if (i+4>il.Length) break; int n = System.BitConverter.ToInt32(il,i); i += 4+4*n; continue; }
        int at = i; i += osz(op.OperandType);
        if (op == System.Reflection.Emit.OpCodes.Newarr) allocSites.Add(dt+"."+m.Name+" newarr");
        else if (op == System.Reflection.Emit.OpCodes.Box) allocSites.Add(dt+"."+m.Name+" box");
        else if (op == System.Reflection.Emit.OpCodes.Newobj) {
            try { int tok = System.BitConverter.ToInt32(il,at);
                  var ctor = m.Module.ResolveMethod(tok, ta, ma);
                  if (ctor != null && ctor.DeclaringType != null && !ctor.DeclaringType.IsValueType)
                      allocSites.Add(dt+"."+m.Name+" newobj "+ctor.DeclaringType.Name); } catch { unresolved.Add(dt+"."+m.Name+" newobj"); } }
        else if (op == System.Reflection.Emit.OpCodes.Call || op == System.Reflection.Emit.OpCodes.Callvirt
              || op == System.Reflection.Emit.OpCodes.Ldftn || op == System.Reflection.Emit.OpCodes.Newobj) {
            try { int tok = System.BitConverter.ToInt32(il,at);
                  var callee = m.Module.ResolveMethod(tok, ta, ma);
                  if (callee != null && callee.DeclaringType != null) {
                      string cns = callee.DeclaringType.Namespace ?? "";
                      string cdt = callee.DeclaringType.Name;
                      if (cns.StartsWith("Laubrary") || cns.StartsWith("UnityEngine") || cns == "" || cns.StartsWith("ZUI")) {
                          if (cns.StartsWith("Laubrary") && !listed.Contains(cdt)) externalTypes.Add(cdt+"."+callee.Name);
                          if (cns.StartsWith("Laubrary")) queue.Enqueue(callee);
                      }
                  } } catch { unresolved.Add(dt+"."+m.Name+" call"); } }
    }
}
sb.AppendLine("closure from PaintTile: methods visited "+visited);
sb.AppendLine("allocation sites in the closure: "+allocSites.Count);
foreach (var a in allocSites) sb.AppendLine("   ALLOC "+a);
sb.AppendLine("Laubrary types reached that are NOT in BT-14's hand-written type list: "+externalTypes.Count);
foreach (var t in externalTypes) sb.AppendLine("   OUTSIDE-LIST "+t);
sb.AppendLine("unresolved tokens: "+unresolved.Count);
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
