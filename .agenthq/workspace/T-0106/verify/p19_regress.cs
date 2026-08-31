var sb = new System.Text.StringBuilder();
sb.AppendLine("== E: the shape stage's OLD Compile(root, phase01, seed) signature ==");
sb.AppendLine("   Nothing is committed, so no literal before/after diff exists. The strongest available substitute:");
sb.AppendLine("   the ONLY way parentForward can perturb the old path is through Mul(parentForward, localToParent),");
sb.AppendLine("   and the old code hardcoded Identity there. So: is Mul(Identity, X) BITWISE X, for adversarial X?");
{
  var rnd = new System.Random(20260831);
  int bad = 0, tested = 0; string firstBad = null;
  System.Func<float> R = () => (float)((rnd.NextDouble()-0.5)*2000.0);
  var vals = new System.Collections.Generic.List<float>{0f,-0f,1f,-1f,1e-30f,-1e-30f,1e30f,-1e30f,float.Epsilon,-float.Epsilon,3.14159265f,-2.71828f};
  for(int i=0;i<12;i++) vals.Add(R());
  for(int a=0;a<vals.Count;a++)
  {
    var m = new Laubrary.Shaper.ShaperMatrix{ m00=vals[a], m01=vals[(a+1)%vals.Count], m02=vals[(a+2)%vals.Count],
                                              m10=vals[(a+3)%vals.Count], m11=vals[(a+4)%vals.Count], m12=vals[(a+5)%vals.Count] };
    var r = Laubrary.Shaper.ShaperMatrix.Mul(Laubrary.Shaper.ShaperMatrix.Identity, m);
    tested++;
    bool same = System.BitConverter.SingleToInt32Bits(r.m00)==System.BitConverter.SingleToInt32Bits(m.m00)
             && System.BitConverter.SingleToInt32Bits(r.m01)==System.BitConverter.SingleToInt32Bits(m.m01)
             && System.BitConverter.SingleToInt32Bits(r.m02)==System.BitConverter.SingleToInt32Bits(m.m02)
             && System.BitConverter.SingleToInt32Bits(r.m10)==System.BitConverter.SingleToInt32Bits(m.m10)
             && System.BitConverter.SingleToInt32Bits(r.m11)==System.BitConverter.SingleToInt32Bits(m.m11)
             && System.BitConverter.SingleToInt32Bits(r.m12)==System.BitConverter.SingleToInt32Bits(m.m12);
    if(!same){ bad++; if(firstBad==null) firstBad = "in " + m.m00 + "," + m.m01 + "," + m.m02 + " -> out " + r.m00 + "," + r.m01 + "," + r.m02; }
  }
  sb.AppendLine("   Mul(Identity, X) bitwise-equal to X: " + (tested-bad) + "/" + tested + " matrices (expected all)" + (firstBad!=null? "   first mismatch: "+firstBad : ""));
}
sb.AppendLine();
sb.AppendLine("   And: does the no-parentForward overload produce a program identical to the Identity-seeded one?");
{
  System.Func<Laubrary.Shaper.ShaperNode> Tree = () => {
    var bag = Laubrary.Shaper.ShaperNode.Bag("B");
    var a = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=20f, ellipseRy=13f},"a");
    a.transform.translate=new UnityEngine.Vector2(7f,-3f); a.transform.rotation=31f; a.transform.scale=new UnityEngine.Vector2(1.3f,0.7f);
    var b = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{kind=Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW=18f, rectHalfH=6f},"b",Laubrary.Shaper.ShaperCombineMode.Subtract);
    b.transform.rotation=-17f; b.shell.enabled=true; b.shell.thickness=3f;
    var c = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{kind=Laubrary.Shaper.ShaperPrimitiveKind.NGon, ngonSides=5, ngonRadius=11f},"c",Laubrary.Shaper.ShaperCombineMode.Intersect);
    c.sweep.enabled=true; c.sweep.startDegrees=20f; c.sweep.extentDegrees=200f;
    bag.children.Add(a); bag.children.Add(b); bag.children.Add(c);
    bag.transform.rotation=12f; bag.transform.translate=new UnityEngine.Vector2(-5f,9f);
    return bag; };
  var p1 = Laubrary.Shaper.ShaperCompiler.Compile(Tree(), 0.41f, 777u);
  var p2 = Laubrary.Shaper.ShaperCompiler.Compile(Tree(), Laubrary.Shaper.ShaperMatrix.Identity, 0.41f, 777u);
  int diffs=0;
  if(p1.ops.Length!=p2.ops.Length) diffs=999999;
  else for(int i=0;i<p1.ops.Length;i++){
    var f = typeof(Laubrary.Shaper.ShaperOp).GetFields();
    foreach(var fi in f){ object v1=fi.GetValue(p1.ops[i]), v2=fi.GetValue(p2.ops[i]);
      if(v1 is float){ if(System.BitConverter.SingleToInt32Bits((float)v1)!=System.BitConverter.SingleToInt32Bits((float)v2)) diffs++; }
      else if(!v1.Equals(v2)) diffs++; } }
  sb.AppendLine("   ops arrays: " + p1.ops.Length + " ops, field-by-field bitwise differences = " + diffs + " (expected 0)");
  sb.AppendLine("   bound " + (p1.bound==p2.bound) + ", stackDepth " + (p1.stackDepth==p2.stackDepth) +
                ", supportBox " + (p1.supportCx==p2.supportCx && p1.supportCy==p2.supportCy && p1.supportHalfW==p2.supportHalfW && p1.supportHalfH==p2.supportHalfH));
  sb.AppendLine("   the new fields on the same program: hasLocalSupport=" + p1.hasLocalSupport +
                " localHalf=" + p1.localSupportHalfW.ToString("F3") + "/" + p1.localSupportHalfH.ToString("F3") +
                " rootInvertible=" + p1.rootInvertible);
}
sb.AppendLine();
sb.AppendLine("   Does the localBox fold feed back into anything the old path used? Recompute the canvas support box");
sb.AppendLine("   independently from the ops' own per-leaf boxes and compare against program.support*:");
{
  var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{kind=Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW=30f, rectHalfH=12f},"R");
  n.transform.rotation=37f; n.transform.translate=new UnityEngine.Vector2(11f,-4f);
  var p = Laubrary.Shaper.ShaperCompiler.Compile(n, 0f, 0u);
  var op = p.ops[0];
  sb.AppendLine("   leaf op box  = (" + op.boxCx.ToString("F5") + "," + op.boxCy.ToString("F5") + ") half " + op.boxHalfW.ToString("F5") + "/" + op.boxHalfH.ToString("F5"));
  sb.AppendLine("   program box  = (" + p.supportCx.ToString("F5") + "," + p.supportCy.ToString("F5") + ") half " + p.supportHalfW.ToString("F5") + "/" + p.supportHalfH.ToString("F5"));
  sb.AppendLine("   equal: " + (op.boxCx==p.supportCx && op.boxCy==p.supportCy && op.boxHalfW==p.supportHalfW && op.boxHalfH==p.supportHalfH));
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r19_regress.txt", sb.ToString());
return "ok";
