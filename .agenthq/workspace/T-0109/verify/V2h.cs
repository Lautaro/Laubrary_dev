using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// V2 - F4's WIRING, driven independently of the audit through the real ShaperFillResolver.PaintTile.
// Five questions the brief asks: (a) is the sheet really reaching the resolver, (b) is FC-2.5 a real
// sum with BOTH terms non-zero, (c) is pointZ base+height and does a POINT LAMP shade a domed layer
// differently from a flat one, (d) does ShaperNormalKind.Profile produce a profile-derived normal end
// to end and is the fallback loud, (e) is `height = null` bit-identical to the pre-fix behaviour.
public static class V2Wire
{
    const int W = 48, H = 40;
    const float Px = 1f;

    static ShaperNode Rect(float hw, float hh, float r)
        => ShaperNode.Primitive(new ShaperPrimitiveDef
        { kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh, rectCornerRadius = r }, "r", ShaperCombineMode.Add);

    class RunR
    {
        public ShaperFillBuffers buf;
        public ShaperLightScene scene;
        public ShaperFillDocument fdoc;
        public ShaperHeightOp hop;
    }

    static RunR Paint(ShaperHeightDef height, ShaperNormalKind nk, float zOffset, float heightDelta)
    {
        int n = W * H;
        var grid = ShaperSampleGrid.Centred(W, H, Px);
        var doc = new ShaperDocument { canvasWidth = W, canvasHeight = H, pixelSize = Px };
        var layer = new ShaperLayer { name = "L", root = Rect(15f, 11f, 3f), height = height, zOffset = new ZUIValue(zOffset) };
        layer.response.normalKind = nk;
        if (heightDelta != 0f)
        {
            layer.root.fill = new ShaperFillDef { kind = ShaperFillKind.Solid, heightDelta = new ZUIValue(heightDelta) };
        }
        doc.layers.Add(layer);

        var lightProg = ShaperLightCompiler.CompileDocument(doc);
        var fdoc = ShaperFillResolver.Resolve(layer.root, doc.phase01, doc.seed, 0.5f * (W - 1) * Px, 0.5f * (H - 1) * Px);
        int k = Mathf.Max(1, fdoc.owners.Count);
        var buf = new ShaperFillBuffers(n, k);
        ShaperProgram layerProg = fdoc.owners.Count > 0 ? fdoc.owners[0].shape : null;
        var scene = ShaperLightCompiler.BindLayer(doc, 0, lightProg, buf.sampleCapacity, buf.ownerCapacity, layerProg, Px);
        ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, W, H, buf,
            new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine }, scene);
        return new RunR { buf = buf, scene = scene, fdoc = fdoc, hop = scene.heightOp != null ? scene.heightOp[0] : default(ShaperHeightOp) };
    }

    public static void RunAll()
    {
        int n = W * H;
        Console.WriteLine("=== V2-WIRE - F4, driven independently through the real ShaperFillResolver.PaintTile ===");
        Console.WriteLine();

        var dome = new ShaperHeightDef {
            technique = ShaperExtrusionTechnique.Dome, bevel = ShaperBevelTechnique.Rounded,
            depth = new ZUIValue(24f), bevelAmount = new ZUIValue(0.3f) };

        // ── (a) + (b): both FC-2.5 terms NON-ZERO simultaneously ──────────────────────────────────
        Console.WriteLine("-- (a)(b) FC-2.5 height_final = height_shape + heightDelta*coverageEff, with BOTH terms non-zero --");
        var withDelta = Paint(dome, ShaperNormalKind.Profile, 5f, 3f);
        var noDelta = Paint(dome, ShaperNormalKind.Profile, 5f, 0f);
        int nzShape = 0, nzDelta = 0, sumOk = 0, bothNz = 0; float worst = 0f;
        for (int i = 0; i < n; i++)
        {
            float shape = withDelta.buf.ownHeight[i];
            float delta = withDelta.buf.heightDelta[i];
            if (shape != 0f) nzShape++;
            if (delta != 0f) nzDelta++;
            if (shape != 0f && delta != 0f) bothNz++;
            // reconstruct the sum from the two published terms
            float ce = 0f;
            // coverageEff is not published; use the identity height == shape + delta*ce for ce in [0,1]
            float diff = withDelta.buf.height[i] - shape;
            if (delta != 0f) ce = diff / delta;
            if (ce >= -1e-4f && ce <= 1f + 1e-4f) sumOk++;
            else worst = Mathf.Max(worst, Mathf.Abs(ce));
        }
        Console.WriteLine("      samples with height_shape != 0        : " + nzShape + "/" + n);
        Console.WriteLine("      samples with heightDelta   != 0        : " + nzDelta + "/" + n);
        Console.WriteLine("      samples with BOTH terms non-zero       : " + bothNz + "/" + n
                          + (bothNz > 0 ? "   <- the sum is genuinely a sum" : "   <- NOT a real sum here"));
        Console.WriteLine("      samples where (height - shape)/delta is a legal coverageEff in [0,1]: " + sumOk + "/" + n
                          + (worst > 0 ? "  worst illegal ce = " + worst.ToString("F4") : ""));
        float maxWith = 0f, maxNo = 0f;
        for (int i = 0; i < n; i++) { maxWith = Mathf.Max(maxWith, withDelta.buf.height[i]); maxNo = Mathf.Max(maxNo, noDelta.buf.height[i]); }
        Console.WriteLine("      max height_final with delta=3: " + maxWith.ToString("F4") + "   with delta=0: " + maxNo.ToString("F4")
                          + "   difference = " + (maxWith - maxNo).ToString("F4"));
        Console.WriteLine();

        // ── (c) pointZ, and whether a POINT LAMP shades domed differently from flat ────────────────
        Console.WriteLine("-- (c) pointZ = base + height, and the observable consequence for a POINT lamp --");
        var hop = noDelta.hop;
        int zMatch = 0, zZero = 0; float maxPz = 0f;
        for (int i = 0; i < n; i++)
        {
            if (Mathf.Abs(noDelta.scene.pointZ[i] - (hop.baseZ + noDelta.buf.ownHeight[i])) <= 1e-3f) zMatch++;
            if (noDelta.scene.pointZ[i] == 0f) zZero++;
            maxPz = Mathf.Max(maxPz, noDelta.scene.pointZ[i]);
        }
        Console.WriteLine("      baseZ = " + hop.baseZ.ToString("F4") + " (HS-7.2: 0*layerSpacing + zOffset 5)   "
                          + zMatch + "/" + n + " match base+height, " + zZero + " still exactly 0, max pointZ " + maxPz.ToString("F4"));

        // A point lamp above the plate, and the same document with a FLAT profile of the same body.
        var flat = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Flat, bevel = ShaperBevelTechnique.None, depth = new ZUIValue(24f) };
        var domeRun = PaintLit(dome);
        var flatRun = PaintLit(flat);
        var nullRun = PaintLit(null);
        int diffPix = 0; double maxDiff = 0;
        for (int i = 0; i < n * 4; i++)
        {
            double d = Math.Abs(domeRun.buf.dst[i] - flatRun.buf.dst[i]);
            if (d > 1e-5) diffPix++;
            if (d > maxDiff) maxDiff = d;
        }
        Console.WriteLine("      point lamp: domed vs flat layer -> " + diffPix + "/" + (n * 4)
                          + " RGBA channels differ, max |delta| = " + maxDiff.ToString("F5")
                          + (diffPix > 0 ? "   <- the height genuinely reaches the shading" : "   <- NOMINAL, height does not reach shading"));
        Console.WriteLine();

        // ── (d) Profile normals end to end, and the loudness of the fallback ──────────────────────
        Console.WriteLine("-- (d) ShaperNormalKind.Profile end to end --");
        int flatN = 0, unit = 0, nan = 0;
        for (int i = 0; i < n; i++)
        {
            float x = noDelta.scene.normal[i * 3], y = noDelta.scene.normal[i * 3 + 1], z = noDelta.scene.normal[i * 3 + 2];
            if (x == 0f && y == 0f && z == 1f) flatN++;
            float len = Mathf.Sqrt(x * x + y * y + z * z);
            if (Mathf.Abs(len - 1f) <= 1e-4f) unit++;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) nan++;
        }
        Console.WriteLine("      degenerate count = " + noDelta.scene.normalDegenerate[0] + "/" + n
                          + ",  exactly (0,0,1): " + flatN + "/" + n + ",  unit to 1e-4: " + unit + "/" + n + ",  NaN: " + nan);
        var noHeightProfile = Paint(null, ShaperNormalKind.Profile, 5f, 0f);
        Console.WriteLine("      INJECTION, same authored Profile with height = null: degenerate count = "
                          + noHeightProfile.scene.normalDegenerate[0] + "/" + n
                          + (noHeightProfile.scene.normalDegenerate[0] == n ? "   <- LOUD" : "   <- NOT loud"));
        var constRun = Paint(dome, ShaperNormalKind.Constant, 5f, 0f);
        int sameAsConst = 0;
        for (int i = 0; i < n * 3; i++) if (noDelta.scene.normal[i] == constRun.scene.normal[i]) sameAsConst++;
        Console.WriteLine("      Profile vs Constant normals identical in " + sameAsConst + "/" + (n * 3)
                          + " components" + (sameAsConst == n * 3 ? "   <- Profile is indistinguishable from Constant (BAD)" : "   <- genuinely different"));
        Console.WriteLine();

        // ── (e) height = null must be BIT-IDENTICAL to the pre-fix behaviour ──────────────────────
        Console.WriteLine("-- (e) ShaperLayer.height = null: is the pre-fix behaviour preserved BIT for BIT? --");
        {
            var nullNo = Paint(null, ShaperNormalKind.Constant, 5f, 0f);
            var nullDelta = Paint(null, ShaperNormalKind.Constant, 5f, 3f);
            int ownHeightNz = 0, pzNz = 0, badSum = 0;
            for (int i = 0; i < n; i++)
            {
                if (nullDelta.buf.ownHeight[i] != 0f) ownHeightNz++;
                if (nullDelta.scene.pointZ[i] != 0f) pzNz++;
            }
            // pre-fix formula: height == 0 + heightDelta*ce, so height/heightDelta must lie in [0,1] and
            // height must be 0 wherever heightDelta is 0.
            for (int i = 0; i < n; i++)
            {
                float d = nullDelta.buf.heightDelta[i];
                if (d == 0f) { if (nullDelta.buf.height[i] != 0f) badSum++; }
                else { float ce = nullDelta.buf.height[i] / d; if (ce < -1e-6f || ce > 1f + 1e-6f) badSum++; }
            }
            int nullHeightNz = 0;
            for (int i = 0; i < n; i++) if (nullNo.buf.height[i] != 0f) nullHeightNz++;
            Console.WriteLine("      height=null: ownHeight non-zero " + ownHeightNz + "/" + n
                              + ",  pointZ non-zero " + pzNz + "/" + n
                              + ",  height non-zero with no fill delta " + nullHeightNz + "/" + n);
            Console.WriteLine("      height=null with delta=3: samples violating the PRE-FIX identity height == 0 + delta*ce: " + badSum + "/" + n);
            Console.WriteLine("      heightOp[0].present = " + nullNo.hop.present + " (HS-1.4 'absent', not a zero-depth stage)");
        }
        Console.WriteLine();

        // ── (f) the root-owner seed: what happens with SEVERAL owners? (fixer's own open question 5) ──
        Console.WriteLine("-- (f) the root-owner seed with SEVERAL owners (the fixer's open question 5) --");
        {
            // a bag with two children, each carrying its own fill -> several owners
            var a = Rect(15f, 11f, 3f);
            var b = Rect(6f, 6f, 0f); b.transform.translate = new Vector2(8f, 0f);
            b.fill = new ShaperFillDef { kind = ShaperFillKind.Solid };
            var root = ShaperNode.Bag("bag", ShaperCombineMode.Add, a, b);
            root.fill = new ShaperFillDef { kind = ShaperFillKind.Solid };
            var doc = new ShaperDocument { canvasWidth = W, canvasHeight = H, pixelSize = Px };
            var layer = new ShaperLayer { name = "L", root = root, height = dome, zOffset = new ZUIValue(5f) };
            doc.layers.Add(layer);
            var grid = ShaperSampleGrid.Centred(W, H, Px);
            var lightProg = ShaperLightCompiler.CompileDocument(doc);
            var fdoc = ShaperFillResolver.Resolve(root, 0f, 0u, 0.5f * (W - 1) * Px, 0.5f * (H - 1) * Px);
            int k = Mathf.Max(1, fdoc.owners.Count);
            var buf = new ShaperFillBuffers(n, k);
            var scene = ShaperLightCompiler.BindLayer(doc, 0, lightProg, buf.sampleCapacity, buf.ownerCapacity,
                                                      fdoc.owners.Count > 0 ? fdoc.owners[0].shape : null, Px);
            ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, W, H, buf,
                new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine }, scene);
            int roots = 0;
            for (int o = 0; o < fdoc.owners.Count; o++) if (fdoc.owners[o].ancestorOwner < 0) roots++;
            Console.WriteLine("      owners = " + fdoc.owners.Count + ",  owners with ancestorOwner < 0 = " + roots);
            for (int o = 0; o < fdoc.owners.Count; o++)
            {
                float mx = 0f; int nz = 0;
                for (int i = 0; i < n; i++) { float v = buf.ownHeight[o * buf.sampleCapacity + i]; if (v != 0f) nz++; if (v > mx) mx = v; }
                Console.WriteLine("        owner " + o + " ancestor=" + fdoc.owners[o].ancestorOwner
                                  + "  ownHeight nz=" + nz + " max=" + mx.ToString("F4")
                                  + "  heightOp.present=" + (scene.heightOp != null ? scene.heightOp[o].present.ToString() : "n/a"));
            }
            int seeded = 0;
            for (int i = 0; i < n; i++) if (buf.height[i] != 0f) seeded++;
            Console.WriteLine("      buf.height non-zero = " + seeded + "/" + n);
            // does the SECOND owner's pointZ get base+height too?
            for (int o = 0; o < Math.Min(fdoc.owners.Count, scene.ownerCapacity); o++)
            {
                int nzp = 0;
                for (int i = 0; i < n; i++) if (scene.pointZ[o * buf.sampleCapacity + i] != 0f) nzp++;
                Console.WriteLine("        owner " + o + " pointZ non-zero = " + nzp + "/" + n);
            }
        }
    }

    static RunR PaintLit(ShaperHeightDef height)
    {
        int n = W * H;
        var grid = ShaperSampleGrid.Centred(W, H, Px);
        var doc = new ShaperDocument { canvasWidth = W, canvasHeight = H, pixelSize = Px };
        var layer = new ShaperLayer { name = "L", root = Rect(15f, 11f, 3f), height = height, zOffset = new ZUIValue(5f) };
        layer.response.normalKind = ShaperNormalKind.Constant;      // isolate pointZ, not the normal
        layer.root.fill = new ShaperFillDef { kind = ShaperFillKind.Solid };
        doc.layers.Add(layer);
        // a POINT lamp close above the plate, so its distance and direction depend on pointZ
        doc.lightRig.lights.Add(new ShaperLight {
            kind = ShaperLightKind.Point, intensity = new ZUIValue(1f),
            posX = new ZUIValue(0f), posY = new ZUIValue(0f), posZ = new ZUIValue(30f),
            range = new ZUIValue(60f) });

        var lightProg = ShaperLightCompiler.CompileDocument(doc);
        var fdoc = ShaperFillResolver.Resolve(layer.root, 0f, 0u, 0.5f * (W - 1) * Px, 0.5f * (H - 1) * Px);
        int k = Mathf.Max(1, fdoc.owners.Count);
        var buf = new ShaperFillBuffers(n, k);
        var scene = ShaperLightCompiler.BindLayer(doc, 0, lightProg, buf.sampleCapacity, buf.ownerCapacity,
                                                  fdoc.owners.Count > 0 ? fdoc.owners[0].shape : null, Px);
        ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, W, H, buf,
            new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine }, scene);
        return new RunR { buf = buf, scene = scene, fdoc = fdoc, hop = scene.heightOp != null ? scene.heightOp[0] : default(ShaperHeightOp) };
    }

    public static void Run() { RunAll(); }
}
