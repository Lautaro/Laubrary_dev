// T-0337 §3 — Fire / Fireball hosted parity with the SIM PARAMETERS MATCHED ON BOTH SIDES.
//
// Round 19 rebuilt T-0272's harness but compared FACTORY DEFAULTS on both sides, which for the two stateful
// simulations are genuinely different parameter sets — so its Fire numbers were not comparable to T-0272's.
// This harness copies EVERY dial of the composite source onto Pyre's own layer, field for field, plus the
// ramp, threshold, contrast, sub-steps and the alpha envelope, then renders both. THE PROBE BODY IS ARCHIVED
// (this file) — the thing T-0272 failed to keep.
//
// Reference : one-layer Pyre spec, shapeForm = Fire / Fireball, through PyreRenderer.RenderFrame.
// Candidate : the composite source's own Render at the same canvas, seed and phase.
// 64x64, seed 1234567, frames 0 / mid / last of an 8-frame document.
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var BFs = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
var pyreT = ZType("Pyre"); var pyreLayerT = ZType("PyreLayer"); var rendT = ZType("PyreRenderer");
var shapeEnum = ZType("ShapeForm");
var renderFrame = rendT.GetMethod("RenderFrame", BFs);
var fillT = null as System.Type;

System.Func<UnityEngine.Color32[], UnityEngine.Color32[], string> diff = (a, b) => {
  int vis = 0, exact = 0, maxCh = 0, covA = 0, covB = 0;
  for (int i = 0; i < a.Length && i < b.Length; i++) {
    if (a[i].a > 0) covA++; if (b[i].a > 0) covB++;
    if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) { exact++;
      if (a[i].a > 0 || b[i].a > 0) { vis++;
        int m = UnityEngine.Mathf.Max(UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(a[i].r-b[i].r), UnityEngine.Mathf.Abs(a[i].g-b[i].g)),
                UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(a[i].b-b[i].b), UnityEngine.Mathf.Abs(a[i].a-b[i].a)));
        if (m > maxCh) maxCh = m; } } }
  return vis + "\t" + exact + "\t" + maxCh + "\t" + covA + "\t" + covB; };

// Shaper-source field name → the PyreLayer field it must equal
var mapFire = new System.Collections.Generic.Dictionary<string,string> {
  {"intensity","fireIntensity"},{"arms","fireArms"},{"armMode","fireArmMode"},{"direction","fireDirection"},
  {"emitterWidth","fireEmitterWidth"},{"emitterInset","fireEmitterInset"},{"heat","fireHeat"},{"fuel","fireFuel"},
  {"pulse","firePulse"},{"flow","fireFlow"},{"buoyancy","fireBuoyancy"},{"curl","fireCurl"},{"curlScale","fireCurlScale"},
  {"flicker","fireFlicker"},{"stretch","fireStretch"},{"pinch","firePinch"},{"breakup","fireBreakup"},
  {"dissipation","fireDissipation"},{"burn","fireBurn"},{"reach","fireReach"},{"edgeCooling","fireEdgeCooling"},
  {"subSteps","fireSteps"},{"threshold","fireThreshold"},{"contrast","fireContrast"},{"alpha","alpha"},
};
var mapBall = new System.Collections.Generic.Dictionary<string,string> {
  {"source","fireballSource"},{"sourceRadius","fireballSourceRadius"},{"cooling","fireballCooling"},
  {"sharpness","fireballSharpness"},{"spread","fireballSpread"},{"reach","fireballReach"},
  {"arms","fireballArms"},{"mirror","fireballMirror"},{"threshold","fireballThreshold"},
  {"contrast","fireballContrast"},{"alpha","alpha"},
};

sb.Append("sim\tmatched\tframe\tvisibleDiff\texactDiff\tmaxCh\tcovPyre\tcovShaper\tunmapped\n");

string[] simNames  = { "Fire", "Fireball" };
string[] simTypes  = { "FireCompositeSource", "FireballCompositeSource" };

for (int s = 0; s < 2; s++)
{
  var srcT = ZType(simTypes[s]); if (srcT == null) { sb.Append(simNames[s]).Append("\tNO TYPE\n"); continue; }
  var map = s == 0 ? mapFire : mapBall;

  for (int pass = 0; pass < 2; pass++)          // pass 0 = factory defaults (round 19's method), pass 1 = MATCHED
  {
    int[] frames = { 0, 1, 2, 3, 4, 5, 6, 7 };
    for (int k = 0; k < frames.Length; k++)
    {
      int fi = frames[k];
      float phase = (float)fi / (N - 1);

      // ── candidate: the composite source ──
      var src = System.Activator.CreateInstance(srcT);
      var simFramesF = srcT.GetField("simFrames", BFi);
      if (simFramesF != null) simFramesF.SetValue(src, N);

      // ── reference: Pyre's own layer ──
      var spec = UnityEngine.ScriptableObject.CreateInstance(pyreT);
      pyreT.GetField("canvasSize", BFi).SetValue(spec, W);
      pyreT.GetField("frameCount", BFi).SetValue(spec, N);
      pyreT.GetField("seed", BFi).SetValue(spec, SEED);
      var layer = System.Activator.CreateInstance(pyreLayerT);
      pyreLayerT.GetField("matteEnabled", BFi).SetValue(layer, false);
      pyreLayerT.GetField("shapeForm", BFi).SetValue(layer, System.Enum.Parse(shapeEnum, simNames[s]));

      var unmapped = new System.Text.StringBuilder();
      if (pass == 1)
      {
        // copy EVERY dial of the source onto the Pyre layer
        foreach (var f in srcT.GetFields(BFi))
        {
          if (f.Name == "simFrames") continue;                      // the document's own clock, not a sim dial
          if (f.Name == "ramp") continue;                           // handled below via shapeFill.gradient
          string target;
          if (!map.TryGetValue(f.Name, out target)) { unmapped.Append(f.Name).Append(' '); continue; }
          var lf = pyreLayerT.GetField(target, BFi);
          if (lf == null) { unmapped.Append(f.Name).Append("→?").Append(target).Append(' '); continue; }
          var v = f.GetValue(src);
          if (lf.FieldType.IsInstanceOfType(v) || v == null) lf.SetValue(layer, v);
          else if (lf.FieldType.IsEnum && v != null) lf.SetValue(layer, System.Enum.ToObject(lf.FieldType, (int)v));
          else unmapped.Append(f.Name).Append("(type)").Append(' ');
        }
        // the ramp: Pyre paints the sim through layer.shapeFill.gradient (PyreRenderer.cs:442, :616)
        var sfF = pyreLayerT.GetField("shapeFill", BFi);
        var sf = sfF.GetValue(layer);
        if (sf == null) { sf = System.Activator.CreateInstance(sfF.FieldType); sfF.SetValue(layer, sf); }
        var gF = sf.GetType().GetField("gradient", BFi);
        var rampF = srcT.GetField("ramp", BFi);
        if (gF != null && rampF != null) gF.SetValue(sf, rampF.GetValue(src));
        else unmapped.Append("ramp ");
      }

      var listT = typeof(System.Collections.Generic.List<>).MakeGenericType(pyreLayerT);
      var list = System.Activator.CreateInstance(listT);
      listT.GetMethod("Add").Invoke(list, new object[]{ layer });
      pyreT.GetField("layers", BFi).SetValue(spec, list);

      var refPx = renderFrame.Invoke(null, new object[]{ spec, fi }) as UnityEngine.Color32[];
      var buf = new UnityEngine.Color32[W*H];
      srcT.GetMethod("Render", BFi).Invoke(src, new object[]{ W, H, phase, (uint)SEED, buf });

      sb.Append(simNames[s]).Append('\t').Append(pass == 1 ? "MATCHED" : "factory-defaults").Append('\t')
        .Append(fi).Append('\t').Append(diff(refPx, buf)).Append('\t').Append(unmapped.ToString()).Append('\n');
      UnityEngine.Object.DestroyImmediate(spec);
    }
  }
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\parity-sims-matched.tsv", sb.ToString());
return sb.ToString();
