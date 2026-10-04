// Golden tests of the GoreLab cut engine: every engine case of GORELAB_GOLDEN.json (project root) must reproduce the prototype's output.
// Agents run these by reflection (never through the Test Runner); each [Test] covers one family of cases.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Laubrary.GoreLab.Tests
{
    public class GoreGoldenTests
    {
        static string s_json;

        static string Json()
        {
            if (s_json != null) return s_json;
            string path = Path.Combine(Directory.GetCurrentDirectory(), GoreGoldenChecker.FileName);
            if (!File.Exists(path)) path = Path.Combine(UnityEngine.Application.dataPath, "..", GoreGoldenChecker.FileName);
            Assert.That(File.Exists(path), "golden file not found: " + path);
            return s_json = File.ReadAllText(path);
        }

        static void Run(params string[] kinds)
        {
            var wanted = new HashSet<string>(kinds);
            List<GoldenCaseResult> results = GoreGoldenChecker.RunAll(Json(), k => wanted.Contains(k));
            Assert.That(results.Count, Is.GreaterThan(0), "no cases of kind " + string.Join(",", kinds));
            var failures = results.FindAll(r => r.status == "FAIL").ConvertAll(r => r.ToString());
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test] public void Primitives_HashRngNoiseMix() { Run("primitives"); }
        [Test] public void Tags_NormalizeAndMirror() { Run("normHead", "mirrorHead"); }
        [Test] public void Solids_LineOfSightIntersections() { Run("makeSolid"); }
        [Test] public void MemberCuts_PlanesCapsulesGroupsMasks() { Run("cutHead3D"); }
        [Test] public void FrameCuts_MergeOwnershipMissing() { Run("cutFrame"); }
        [Test] public void FrameMirroring_TagsMasksPixels() { Run("mirrorFrame"); }
        [Test] public void BakeFlow_FramesFromHit() { Run("deathBake"); }

        [Test]
        public void FrameResult_IsReusedWithoutGrowing()
        {
            var grid = new GoreGrid(8, 8);
            for (int i = 0; i < grid.px.Length; i++) grid.px[i] = GoreDefaults.Rgb(120, 80, 60);
            var tag = new MemberTag { cx = 4, cy = 4, rx = 3, ry = 3, n = 2, angle = -Math.PI / 2, uy = -1, fz = 1 };
            GoreTagMath.Normalize(ref tag);
            var frame = new GoreFrameInput { grid = grid, members = new[] { new GoreMemberInput { present = true, tag = tag } } };
            var removers = new List<GoreRemover> { GoreRemovers.Plane(0, 1, 0, 0, 0, 0) };
            var result = new GoreFrameResult();
            GoreCut.CutFrame(frame, removers, GoreCutConfig.Default(), GoreStyle.Fleshy(), result);
            uint[] body = result.body; int changed = result.changed, pieces = result.pieces.Count;
            Assert.That(changed, Is.GreaterThan(0));
            GoreCut.CutFrame(frame, removers, GoreCutConfig.Default(), GoreStyle.Fleshy(), result);
            Assert.That(result.body, Is.SameAs(body));
            Assert.That(result.changed, Is.EqualTo(changed));
            Assert.That(result.pieces.Count, Is.EqualTo(pieces));
        }
    }
}
