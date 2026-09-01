if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
System.Type Find(string nm){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(nm); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.ShaperFillResolver");
foreach (var m in new string[]{ "PaintTile", "Encode" }) {
    var mi = t.GetMethod(m, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    var il = mi.GetMethodBody().GetILAsByteArray();
    int newobj = 0, newarr = 0, box = 0;
    for (int i = 0; i < il.Length; i++) {
        if (il[i] == 0x73) newobj++;
        if (il[i] == 0x8D) newarr++;
        if (il[i] == 0x8C) box++;
    }
    sb.AppendLine("  " + m + ": " + il.Length + " B IL, newobj(0x73)=" + newobj +
                  " newarr(0x8D)=" + newarr + " box(0x8C)=" + box +
                  "   (byte-frequency scan is an UPPER bound - an operand byte can collide with an opcode value, so a zero is conclusive)");
}
return sb.ToString();
