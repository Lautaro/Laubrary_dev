var sb = new System.Text.StringBuilder();
string[] names = { "ShaperLightLaw","ShaperLightRig","ShaperLight","ShaperLightResponse","ShaperLayer",
  "ShaperDocument","ShaperNormals","ShaperNormalOp","ShaperLightCompiler","ShaperLightScene",
  "ShaperLightProgram","ShaperSolids","ShaperSolidDef","ShaperSolidOp","ShaperSolidGeometry",
  "ShaperLightRigCompiled","ShaperResponseCompiled","ShaperLightCompiled" };
foreach (var nm in names) {
  var t = System.Type.GetType("Laubrary.Shaper." + nm + ", com.Lautaro-Arino.Laubrary.Shaper");
  sb.Append(nm + "=" + (t==null?"NULL":"ok") + "; ");
}
var pt = typeof(Laubrary.Shaper.ShaperFillResolver).GetMethods();
int overloads = 0; foreach (var m in pt) if (m.Name=="PaintTile") overloads++;
sb.AppendLine();
sb.AppendLine("PaintTile overloads=" + overloads);
var sh = typeof(Laubrary.Shaper.ShaperLightLaw).GetMethod("Shade");
var ps = sh.GetParameters();
sb.AppendLine("Shade params=" + ps.Length);
foreach (var p in ps) { var pt2 = p.ParameterType; var el = pt2.IsByRef ? pt2.GetElementType() : pt2;
  sb.Append(p.Name + ":" + el.Name + (el.IsValueType?"":"[REF!]") + (el.IsArray?"[ARRAY!]":"") + " "); }
return sb.ToString();
