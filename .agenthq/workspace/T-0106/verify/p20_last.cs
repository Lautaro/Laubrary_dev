var sb = new System.Text.StringBuilder();
sb.AppendLine("== E detail: characterise the 4/24 Mul(Identity,X) mismatches ==");
{
  var vals = new float[]{0f,-0f,1f,-1f,1e-30f,-1e-30f,1e30f,-1e30f,float.Epsilon,-float.Epsilon,3.14159265f,-2.71828f};
  int signOfZero=0, other=0;
  for(int a=0;a<vals.Length;a++){
    var m = new Laubrary.Shaper.ShaperMatrix{ m00=vals[a], m01=vals[(a+1)%vals.Length], m02=vals[(a+2)%vals.Length],
                                              m10=vals[(a+3)%vals.Length], m11=vals[(a+4)%vals.Length], m12=vals[(a+5)%vals.Length] };
    var r = Laubrary.Shaper.ShaperMatrix.Mul(Laubrary.Shaper.ShaperMatrix.Identity, m);
    float[] mi = { m.m00,m.m01,m.m02,m.m10,m.m11,m.m12 };
    float[] ri = { r.m00,r.m01,r.m02,r.m10,r.m11,r.m12 };
    for(int i=0;i<6;i++){
      int bm=System.BitConverter.SingleToInt32Bits(mi[i]), br=System.BitConverter.SingleToInt32Bits(ri[i]);
      if(bm==br) continue;
      if(mi[i]==0f && ri[i]==0f) signOfZero++; else other++;
    } }
  sb.AppendLine("   component-level mismatches that are ONLY the sign bit of a zero (-0.0 -> +0.0): " + signOfZero);
  sb.AppendLine("   component-level mismatches that are anything else:                              " + other + " (expected 0)");
  sb.AppendLine("   -0.0 and +0.0 compare equal and are indistinguishable downstream here (nothing divides by a");
  sb.AppendLine("   matrix component; TryInvert divides by the determinant, which is 0 either way), and a transform");
  sb.AppendLine("   block cannot produce -0.0 through translate/rotate/scale/skew authoring in the first place.");
}
sb.AppendLine();
sb.AppendLine("== D limitation: the Fixed-space anchor assumes the CANVAS IS CENTRED ON THE ORIGIN ==");
{
  int W=64,H=64; float Px=1f;
  var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{
    kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=20f, ellipseRy=20f }, "D");
  n.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient,
    gradientMode=Laubrary.Shaper.ShaperGradientMode.Radial, space=Laubrary.Shaper.ShaperFillSpace.Fixed };
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(n, 0f, 0u, 0.5f*(W-1)*Px, 0.5f*(H-1)*Px);
  var op = doc.owners[0].fill.op;
  sb.AppendLine("   Fixed anchor bake: anchorC = (" + op.anchorCx.ToString("F3") + ", " + op.anchorCy.ToString("F3") +
                "), invH = (" + op.invHx.ToString("F5") + ", " + op.invHy.ToString("F5") + ")");
  var centred = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  sb.AppendLine("   ShaperSampleGrid.Centred(64,64,1) origin = (" + centred.originX.ToString("F2") + ", " + centred.originY.ToString("F2") +
                ") -> canvas centre = (" + (centred.originX + 0.5f*(W-1)*Px).ToString("F2") + ", " + (centred.originY + 0.5f*(H-1)*Px).ToString("F2") + ")");
  float u,v; Laubrary.Shaper.ShaperFillOps.Anchor(in op, 0f, 0f, out u, out v);
  sb.AppendLine("   Fixed u,v at canvas (0,0)   = (" + u.ToString("F4") + ", " + v.ToString("F4") + ")  (0,0 is correct only because Centred puts the canvas centre at the origin)");
  sb.AppendLine("   ShaperFillAnchor carries canvasHalfW/H but NO canvas CENTRE, so a host using a grid whose origin");
  sb.AppendLine("   is not -halfExtent would get an off-centre Fixed pattern. Every shipped path uses Centred, so this");
  sb.AppendLine("   is latent, not live. FC-1.6 says 'normalised by the layer canvas half-extents' and does not say where");
  sb.AppendLine("   the canvas centre is, so the contract does not catch it either.");
}
sb.AppendLine();
sb.AppendLine("== FT-20 legend claim: are contact-sheet cells 14 and 15 (Fixed/Uniform vs Fixed/Stretch) really identical? ==");
{
  int W=96,H=96; float Px=1f; float chw=0.5f*(W-1)*Px, chh=0.5f*(H-1)*Px;
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  System.Func<Laubrary.Shaper.ShaperFillFit,float[]> Run = fit => {
    var d = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{
      kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=34f, ellipseRy=34f }, "Wide");
    d.transform.scale=new UnityEngine.Vector2(1.5f,0.55f); d.transform.rotation=25f;
    var g=new ZuiGradient(); g.gradient=new UnityEngine.Gradient();
    g.gradient.SetKeys(new[]{new UnityEngine.GradientColorKey(UnityEngine.Color.red,0f),new UnityEngine.GradientColorKey(UnityEngine.Color.yellow,1f)},
                       new[]{new UnityEngine.GradientAlphaKey(1f,0f),new UnityEngine.GradientAlphaKey(1f,1f)}); g.EnsureTransformAnim();
    d.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient,
      gradientMode=Laubrary.Shaper.ShaperGradientMode.Radial, gradient=g,
      space=Laubrary.Shaper.ShaperFillSpace.Fixed, fit=fit };
    var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(d, 0f, 0u, chw, chh);
    var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
    Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,
      new Laubrary.Shaper.ShaperFillSheets{published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine});
    var c=new float[W*H*4]; System.Array.Copy(buf.dst,c,c.Length); return c; };
  var a=Run(Laubrary.Shaper.ShaperFillFit.Uniform); var b=Run(Laubrary.Shaper.ShaperFillFit.Stretch);
  int bad=0; for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) bad++;
  sb.AppendLine("   Fixed/Uniform vs Fixed/Stretch on a SQUARE canvas: " + bad + " floats differ (expected 0 - one divisor vs two identical divisors)");
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r20_last.txt", sb.ToString());
return "ok";
