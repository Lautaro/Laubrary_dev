var sb = new System.Text.StringBuilder();
float h = Laubrary.Shaper.ShaperField.HalfBand(0f, 1f);
{
    double worst = 0; int nonZero = 0; int total = 0; float worstD = 0; int bitMismatch = 0;
    for (int i = 0; i <= 400000; i++)
    {
        float d = -1.5f + (3.0f * i) / 400000f;
        float a = Laubrary.Shaper.ShaperField.Coverage(d, h);
        float b = Laubrary.Shaper.ShaperField.Coverage(-d, h);
        float s = a + b;
        double e = System.Math.Abs((double)s - 1.0);
        total++;
        if (s != 1f) { nonZero++; if (e > worst) { worst = e; worstD = d; } }
        if (System.BitConverter.SingleToInt32Bits(s) != System.BitConverter.SingleToInt32Bits(1f)) bitMismatch++;
    }
    sb.AppendLine("A kernel complement sweep: samples " + total + "  sum!=1: " + nonZero
        + "  bitDiffs " + bitMismatch + "  max|err| " + worst.ToString("G9") + " at d=" + worstD.ToString("G9"));
}
{
    var kinds = new Laubrary.Shaper.ShaperPrimitiveKind[] {
        Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, Laubrary.Shaper.ShaperPrimitiveKind.Rect,
        Laubrary.Shaper.ShaperPrimitiveKind.Diamond, Laubrary.Shaper.ShaperPrimitiveKind.Triangle,
        Laubrary.Shaper.ShaperPrimitiveKind.Capsule, Laubrary.Shaper.ShaperPrimitiveKind.NGon,
        Laubrary.Shaper.ShaperPrimitiveKind.Star };
    var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(256, 256, 1f);
    float hb = Laubrary.Shaper.ShaperField.HalfBand(grid.edgeSoftness, grid.pixelSize);
    foreach (var kind in kinds)
    {
        var pd = new Laubrary.Shaper.ShaperPrimitiveDef(); pd.kind = kind;
        var node = Laubrary.Shaper.ShaperNode.Primitive(pd, kind.ToString());
        var prog = Laubrary.Shaper.ShaperCompiler.Compile(node, Laubrary.Shaper.ShaperMatrix.Identity, 0f, 0u);
        var stack = prog.NewStack();
        var bIn = new Laubrary.Shaper.ShaperResolvedBorder();
        bIn.live = true; bIn.width = 6f; bIn.reach = 0f;
        bIn.alignment = Laubrary.Shaper.ShaperShellAlignment.Inward; bIn.joinsCoverage = false;
        var bOut = new Laubrary.Shaper.ShaperResolvedBorder();
        bOut.live = true; bOut.width = 6f; bOut.reach = 6f;
        bOut.alignment = Laubrary.Shaper.ShaperShellAlignment.Outward; bOut.joinsCoverage = false;
        var sIn = Laubrary.Shaper.ShaperBorder.CompileStrip(prog, bIn, false);
        var sOut = Laubrary.Shaper.ShaperBorder.CompileStrip(prog, bOut, false);
        var st2 = sIn.NewStack(); var st3 = sOut.NewStack();
        double worstIn = 0, worstOut = 0; int ring = 0; int bitIn = 0, bitOut = 0;
        for (int j = 0; j < 256; j++)
        for (int i = 0; i < 256; i++)
        {
            float x = grid.X(i), y = grid.Y(j);
            float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog, x, y, stack);
            if (Laubrary.Shaper.ShaperField.IsEmpty(d)) continue;
            if (UnityEngine.Mathf.Abs(d) > 1.0f) continue;
            ring++;
            float cShape = Laubrary.Shaper.ShaperField.Coverage(d, hb);
            float cIn = Laubrary.Shaper.ShaperField.Coverage(Laubrary.Shaper.ShaperEvaluator.Distance(sIn, x, y, st2), hb);
            float cOut = Laubrary.Shaper.ShaperField.Coverage(Laubrary.Shaper.ShaperEvaluator.Distance(sOut, x, y, st3), hb);
            double eIn = System.Math.Abs((double)cIn - cShape);
            double eOut = System.Math.Abs((double)(cOut + cShape) - 1.0);
            if (eIn > worstIn) worstIn = eIn;
            if (eOut > worstOut) worstOut = eOut;
            if (System.BitConverter.SingleToInt32Bits(cIn) != System.BitConverter.SingleToInt32Bits(cShape)) bitIn++;
            if (System.BitConverter.SingleToInt32Bits(cOut + cShape) != System.BitConverter.SingleToInt32Bits(1f)) bitOut++;
        }
        sb.AppendLine("B " + kind + "  ring " + ring
            + "  inward max|diff| " + worstIn.ToString("G9") + " bitDiffs " + bitIn
            + "  |  outward max|sum-1| " + worstOut.ToString("G9") + " bitDiffs " + bitOut);
    }
}
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
