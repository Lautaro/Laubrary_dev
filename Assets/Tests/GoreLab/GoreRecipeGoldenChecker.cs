// Replays the generator cases of GORELAB_GOLDEN.json (planeFromSwipe, shotFromSwipe, straightShot, gashFromSwipe) through the wound recipes and
// compares the removers they produce with the prototype's, field by field and in order. Plain C# (no NUnit) so it also runs in a console harness.
//
// How a case becomes a recipe call: the prototype's generators take a member tag, the canvas offset of the sprite and a seed that the caller has
// already mixed (+7919 for the torso). A recipe gets a WoundContext and mixes the member offset itself (member index * 7919), so a torso case is
// run as member 1 with ctx.seed = seed - 7919, and a head case as member 0 with ctx.seed = seed.
// Two coordinate embeddings are supported: Canvas (the tag centre moved by the canvas offset, the sprite placed on the canvas: the same arithmetic
// as the prototype) and SpriteLocal (swipe moved into sprite pixels, as GoreBody and the editor call the recipes). They differ only by rounding.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Laubrary.GoreLab.Tests
{
    public enum RecipeEmbedding { Canvas, SpriteLocal }

    public static class GoreRecipeGoldenChecker
    {
        public static readonly string[] Kinds = { "planeFromSwipe", "shotFromSwipe", "straightShot", "gashFromSwipe" };
        const double Tolerance = 1e-9;
        const int TorsoSeedOffset = 7919;

        public static List<GoldenCaseResult> RunAll(string jsonText, RecipeEmbedding embedding, Func<string, bool> kindFilter = null)
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(jsonText);
            var sprites = (Dictionary<string, object>)root["sprites"];
            var results = new List<GoldenCaseResult>();
            foreach (var co in (List<object>)root["cases"])
            {
                var c = (Dictionary<string, object>)co;
                string kind = (string)c["kind"], id = (string)c["id"];
                if (Array.IndexOf(Kinds, kind) < 0) continue;
                if (kindFilter != null && !kindFilter(kind)) continue;
                var r = new GoldenCaseResult { id = id, kind = kind };
                var errs = new List<string>();
                try
                {
                    switch (kind)
                    {
                        case "planeFromSwipe": CheckPlanes(c, embedding, errs); break;
                        case "shotFromSwipe": CheckShot(c, embedding, errs); break;
                        case "straightShot": CheckStraight(c, sprites, embedding, errs); break;
                        case "gashFromSwipe": CheckGash(c, sprites, embedding, errs); break;
                    }
                }
                catch (Exception ex) { errs.Add("exception: " + ex); }
                r.status = errs.Count == 0 ? "PASS" : "FAIL";
                r.detail = errs.Count == 0 ? "" : string.Join(" | ", errs.GetRange(0, Math.Min(6, errs.Count))) + (errs.Count > 6 ? $" | (+{errs.Count - 6} more)" : "");
                results.Add(r);
            }
            return results;
        }

        // ------------------------------------------------------------------ cases

        static void CheckPlanes(Dictionary<string, object> c, RecipeEmbedding emb, List<string> errs)
        {
            var inp = Obj(c["input"]);
            var tag = GoreGoldenChecker.Tag(Obj(inp["tag"]));
            int sx = (int)Num(inp["sx"]), sy = (int)Num(inp["sy"]);
            var subs = Arr(inp["cases"]); var want = Arr(c["expected"]);
            if (subs.Count != want.Count) { errs.Add("golden file: cases/expected length differ"); return; }
            for (int k = 0; k < subs.Count; k++)
            {
                var s = Obj(subs[k]);
                var ctx = Context(tag, 0, sx, sy, null, Arr(s["p0"]), Arr(s["p1"]), 0, emb);
                ctx.flipSide = (bool)s["flipInvert"];
                var got = new List<GoreRemover>();
                new SliceRecipe().Generate(ctx, got);
                var list = new List<object>(); if (want[k] != null) list.Add(want[k]);
                Compare("case " + k, ctx, 0, got, list, errs);
            }
        }

        static void CheckShot(Dictionary<string, object> c, RecipeEmbedding emb, List<string> errs)
        {
            var inp = Obj(c["input"]); var cfg = Obj(inp["cfg"]);
            var tag = GoreGoldenChecker.Tag(Obj(inp["tag"]));
            int member = MemberOf(tag);
            var ctx = Context(tag, member, (int)Num(inp["sx"]), (int)Num(inp["sy"]), null, Arr(inp["p0"]), Arr(inp["p1"]), Seed(inp, member), emb);
            var recipe = new ShotgunRecipe
            {
                pellets = (int)Num(cfg["pellets"]), coneDeg = Num(cfg["coneDeg"]), energy = Num(cfg["energy"]), radius = Num(cfg["radius"]),
                toughness = Num(cfg["toughness"]), rangeFalloff = Num(cfg["rangeFalloff"]), straightOn = false,
            };
            var got = new List<GoreRemover>();
            recipe.Generate(ctx, got);
            Compare("", ctx, member, got, Arr(c["expected"]), errs);
        }

        static void CheckStraight(Dictionary<string, object> c, Dictionary<string, object> sprites, RecipeEmbedding emb, List<string> errs)
        {
            var inp = Obj(c["input"]); var cfg = Obj(inp["cfg"]);
            var tag = GoreGoldenChecker.Tag(Obj(inp["tag"]));
            int member = MemberOf(tag), count = (int)Num(inp["count"]);
            var ctx = Context(tag, member, sprites, inp, Seed(inp, member), emb);
            string scorer = (string)inp["scorer"];
            if (scorer == "preferEast") ctx.holeVisibility = op => Math.Max(0, Math.Min(1, (op.ax + 1) / 2));
            else if (scorer != "none") { errs.Add("unknown scorer " + scorer); return; }

            var got = new List<GoreRemover>();
            if (count == 1)
            {
                var bullet = new BulletRecipe
                {
                    energy = Num(cfg["energy"]), radius = Num(cfg["radius"]), toughness = Num(cfg["toughness"]),
                    rangeFalloff = Num(cfg["rangeFalloff"]), straightDepth = Num(cfg["straightDepth"]),
                };
                bullet.Generate(ctx, got);
            }
            else
            {
                var shotgun = new ShotgunRecipe
                {
                    pellets = count, coneDeg = Num(cfg["coneDeg"]), energy = Num(cfg["energy"]), radius = Num(cfg["radius"]), toughness = Num(cfg["toughness"]),
                    rangeFalloff = Num(cfg["rangeFalloff"]), straightDepth = Num(cfg["straightDepth"]), straightOn = true,
                };
                shotgun.Generate(ctx, got);
            }
            Compare("", ctx, member, got, Arr(c["expected"]), errs);
        }

        static void CheckGash(Dictionary<string, object> c, Dictionary<string, object> sprites, RecipeEmbedding emb, List<string> errs)
        {
            var inp = Obj(c["input"]); var cfg = Obj(inp["cfg"]);
            var tag = GoreGoldenChecker.Tag(Obj(inp["tag"]));
            int member = MemberOf(tag);
            var ctx = Context(tag, member, sprites, inp, Seed(inp, member), emb);
            var recipe = new CutRecipe { gashDepth = Num(cfg["gashDepth"]), gashWidth = Num(cfg["gashWidth"]), gashWobble = Num(cfg["gashWobble"]) };
            var got = new List<GoreRemover>();
            recipe.Generate(ctx, got);
            Compare("", ctx, member, got, Arr(c["expected"]), errs);
        }

        // ------------------------------------------------------------------ building the context

        // The golden tags are the prototype's head (ball) and torso (box); the torso is member 1 of the default rig.
        static int MemberOf(in MemberTag t) { return t.kind == MemberKind.Box ? 1 : 0; }

        static int Seed(Dictionary<string, object> inp, int member) { return (int)Num(inp["seed"]) - TorsoSeedOffset * member; }

        static WoundContext Context(in MemberTag tag, int member, Dictionary<string, object> sprites, Dictionary<string, object> inp, int seed, RecipeEmbedding emb)
        {
            var place = Arr(inp["place"]); var canvas = Obj(inp["canvas"]);
            int sx = (int)Num(place[0]), sy = (int)Num(place[1]);
            var sprite = GoreGoldenChecker.Sprite(sprites, (string)inp["sprite"]);
            var grid = emb == RecipeEmbedding.Canvas ? GoreGoldenChecker.Place(sprite, (int)Num(canvas["W"]), (int)Num(canvas["H"]), sx, sy) : sprite;
            return Context(tag, member, sx, sy, grid, Arr(inp["p0"]), Arr(inp["p1"]), seed, emb);
        }

        static WoundContext Context(MemberTag tag, int member, int sx, int sy, GoreGrid grid, List<object> p0, List<object> p1, int seed, RecipeEmbedding emb)
        {
            var ctx = new WoundContext { group = 3, seed = seed, grid = grid, cut = GoreCutConfig.Default() };
            if (emb == RecipeEmbedding.Canvas)
            {
                tag.cx += sx; tag.cy += sy;   // the prototype's cx = h.c.x + sx
                ctx.p0x = Num(p0[0]); ctx.p0y = Num(p0[1]); ctx.p1x = Num(p1[0]); ctx.p1y = Num(p1[1]);
            }
            else
            {
                ctx.p0x = Num(p0[0]) - sx; ctx.p0y = Num(p0[1]) - sy; ctx.p1x = Num(p1[0]) - sx; ctx.p1y = Num(p1[1]) - sy;
            }
            ctx.members = new GoreMemberInput[member + 1];
            ctx.members[member] = new GoreMemberInput { present = true, tag = tag };
            return ctx;
        }

        // ------------------------------------------------------------------ comparison

        static void Compare(string what, WoundContext ctx, int member, List<GoreRemover> got, List<object> want, List<string> errs)
        {
            string w = what.Length > 0 ? what + ": " : "";
            if (got.Count != want.Count) { errs.Add($"{w}{got.Count} removers vs {want.Count}"); return; }
            for (int i = 0; i < got.Count; i++)
            {
                var g = got[i]; var e = Obj(want[i]); string p = $"{w}[{i}]";
                if (g.group != ctx.group) errs.Add($"{p}.group {g.group}");
                if (g.member != member) errs.Add($"{p}.member {g.member}");
                if ((string)e["t"] == "plane")
                {
                    if (g.kind != RemoverKinds.Plane) { errs.Add($"{p}.kind {g.kind} (want plane)"); continue; }
                    var N = Arr(e["N"]);
                    Near(p + ".N.x", g.nx, Num(N[0]), errs); Near(p + ".N.y", g.ny, Num(N[1]), errs); Near(p + ".N.z", g.nz, Num(N[2]), errs);
                    Near(p + ".d", g.d, Num(e["d"]), errs);
                }
                else
                {
                    if (g.kind != RemoverKinds.Capsule) { errs.Add($"{p}.kind {g.kind} (want capsule)"); continue; }
                    var a = Arr(e["a"]); var b = Arr(e["b"]);
                    Near(p + ".a.x", g.ax, Num(a[0]), errs); Near(p + ".a.y", g.ay, Num(a[1]), errs); Near(p + ".a.z", g.az, Num(a[2]), errs);
                    Near(p + ".b.x", g.bx, Num(b[0]), errs); Near(p + ".b.y", g.by, Num(b[1]), errs); Near(p + ".b.z", g.bz, Num(b[2]), errs);
                    Near(p + ".r", g.r, Num(e["r"]), errs);
                    double bx = 0, by = 0;
                    if (e.ContainsKey("back") && e["back"] != null) { var bk = Arr(e["back"]); bx = Num(bk[0]); by = Num(bk[1]); }
                    Near(p + ".back.x", g.backX, bx, errs); Near(p + ".back.y", g.backY, by, errs);
                }
            }
        }

        static void Near(string what, double got, double want, List<string> errs)
        {
            if (!(Math.Abs(got - want) <= Tolerance * Math.Max(1, Math.Abs(want))))
                errs.Add($"{what} {got.ToString("R", CultureInfo.InvariantCulture)} vs {want.ToString("R", CultureInfo.InvariantCulture)}");
        }

        static Dictionary<string, object> Obj(object o) { return (Dictionary<string, object>)o; }
        static List<object> Arr(object o) { return (List<object>)o; }
        static double Num(object o) { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
    }
}
