// Golden tests of the wound recipes: the generator cases of GORELAB_GOLDEN.json (project root) must come out of the recipes exactly as the
// prototype produced them, plus the recipe facts that have no golden case (neck plane, discovery, bullet member choice, mirrored rig input).
// Agents run these by reflection (never through the Test Runner).
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Laubrary.GoreLab.Tests
{
    public class GoreRecipeGoldenTests
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

        static void Run(RecipeEmbedding embedding)
        {
            List<GoldenCaseResult> results = GoreRecipeGoldenChecker.RunAll(Json(), embedding);
            Assert.That(results.Count, Is.EqualTo(7), "expected the 7 generator cases (1 plane set, 1 shotgun, 3 straight-on, 2 knife)");
            var failures = results.FindAll(r => r.status != "PASS").ConvertAll(r => r.ToString());
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        /// <summary>Same arithmetic as the prototype (the tag centre moved onto its canvas).</summary>
        [Test] public void Generators_MatchPrototype_CanvasCoordinates() { Run(RecipeEmbedding.Canvas); }

        /// <summary>As GoreBody and the editor call them (swipe in sprite pixels); differs from the prototype only by rounding.</summary>
        [Test] public void Generators_MatchPrototype_SpriteCoordinates() { Run(RecipeEmbedding.SpriteLocal); }

        static MemberTag Head()
        {
            var t = new MemberTag { cx = 12, cy = 6, rx = 5, ry = 5.5, n = 2, angle = -Math.PI / 2, uy = -1, fz = 1 };
            GoreTagMath.Normalize(ref t);
            return t;
        }

        static MemberTag Torso()
        {
            var t = new MemberTag { kind = MemberKind.Box, cx = 12, cy = 16.5, rx = 6, ry = 6.5, rz = 4, n = 4, angle = -Math.PI / 2, uy = -1, fz = 1 };
            GoreTagMath.Normalize(ref t);
            return t;
        }

        [Test]
        public void RemoveHead_IsThePrototypeNeckPlane_OnlyOnce()
        {
            var ctx = new WoundContext
            {
                group = 4, cut = GoreCutConfig.Default(),
                members = new[] { new GoreMemberInput { present = true, tag = Head() }, new GoreMemberInput { present = true, tag = Torso() } },
            };
            var made = new List<GoreRemover>();
            new RemoveHeadRecipe().Generate(ctx, made);
            Assert.That(made.Count, Is.EqualTo(1));
            var r = made[0];
            Assert.That(r.kind, Is.EqualTo(RemoverKinds.Plane));
            Assert.That(r.member, Is.EqualTo(0));
            Assert.That(r.group, Is.EqualTo(4));
            Assert.That(new[] { r.nx, r.ny, r.nz, r.d }, Is.EqualTo(new[] { 0.0, 1.0, 0.0, -0.8 }));   // N = (0,1,0), d = -neckDepth (0.8)

            ctx.existing.AddRange(made);
            var again = new List<GoreRemover>();
            new RemoveHeadRecipe().Generate(ctx, again);
            Assert.That(again, Is.Empty, "a second Remove head must not cut again");
        }

        [Test]
        public void Discover_FindsTheFiveStandardRecipesFirst()
        {
            var found = GoreRecipes.Discover();
            var standard = new[] { typeof(SliceRecipe), typeof(CutRecipe), typeof(BulletRecipe), typeof(ShotgunRecipe), typeof(RemoveHeadRecipe) };
            Assert.That(found.Count, Is.GreaterThanOrEqualTo(standard.Length));
            for (int i = 0; i < standard.Length; i++) Assert.That(found[i], Is.EqualTo(standard[i]));
            var defaults = GoreRecipes.CreateDefaultList();
            Assert.That(defaults.ConvertAll(r => r.GetType()), Is.EqualTo(standard));
        }

        [Test]
        public void Bullet_PicksTheMemberFromShotCounterAndRigSeed()
        {
            // A vertical line through both members: each would take a hole, so the prototype's rule floor(Hash(shotCounter, 3, cfg.seed) * 2) % 2 decides.
            var seen = new HashSet<int>();
            for (int counter = 0; counter < 12; counter++)
            {
                var cut = GoreCutConfig.Default();
                var ctx = new WoundContext
                {
                    p0x = 12, p0y = 0, p1x = 12, p1y = 30, group = 1, cut = cut, shotCounter = counter, seed = cut.seed * 131 + counter,
                    members = new[] { new GoreMemberInput { present = true, tag = Head() }, new GoreMemberInput { present = true, tag = Torso() } },
                };
                var made = new List<GoreRemover>();
                new BulletRecipe().Generate(ctx, made);
                Assert.That(made.Count, Is.EqualTo(1));
                int want = (int)Math.Floor(GoreRng.Hash(counter, 3, cut.seed) * 2) % 2;
                Assert.That(made[0].member, Is.EqualTo(want), "shot " + counter);
                seen.Add(made[0].member);
            }
            Assert.That(seen.Count, Is.EqualTo(2), "over 12 shots both members should be hit at least once");
        }

        [Test]
        public void RigInputs_Mirrored_MatchesTheEngineMirror()
        {
            const int w = 5, h = 4;
            var tag = new MemberTag { cx = 1.5, cy = 2, rx = 1.5, ry = 1.2, n = 2, angle = -1.2, ux = 0.3, uy = -0.9, uz = 0.2, fx = 0.4, fy = 0.1, fz = 0.9 };
            GoreTagMath.Normalize(ref tag);
            var frame = new GoreFrameTags();
            frame.members.Add(new GoreMemberFrame { present = true, tag = tag, behind = new[] { 0, 6, 13 }, exempt = new[] { 4, 19 } });
            frame.members.Add(new GoreMemberFrame { present = true, skip = true, tag = Torso() });

            var plain = GoreRigInputs.Build(frame, w, h, false);
            var mirrored = GoreRigInputs.Build(frame, w, h, true);
            GoreCut.MirrorFrame(new GoreGrid(w, h), plain, out _, out var engine);

            Assert.That(mirrored.Length, Is.EqualTo(engine.Length));
            for (int m = 0; m < engine.Length; m++)
            {
                Assert.That(mirrored[m].present, Is.EqualTo(engine[m].present));
                Assert.That(mirrored[m].skip, Is.EqualTo(engine[m].skip));
                Assert.That(mirrored[m].tag, Is.EqualTo(engine[m].tag), "tag of member " + m);
                Assert.That(mirrored[m].behind, Is.EqualTo(engine[m].behind), "behind mask of member " + m);
                Assert.That(mirrored[m].exempt, Is.EqualTo(engine[m].exempt), "exempt mask of member " + m);
            }
            // Spot check against the rule itself: pixel (x, y) moves to (w-1-x, y); index 6 = (1,1) -> (3,1) = 8.
            Assert.That(mirrored[0].behind[8], Is.EqualTo(1));
            Assert.That(mirrored[0].behind[6], Is.EqualTo(0));
        }
    }
}
