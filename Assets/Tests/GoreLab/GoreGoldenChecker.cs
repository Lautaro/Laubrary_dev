// Replays every engine case of GORELAB_GOLDEN.json (produced by running the web prototype) through the C# engine and compares the results.
// Plain C# (no Unity, no NUnit) so the same file runs in the console harness outside the editor and in the editor tests.
// Generator cases (planeFromSwipe, shotFromSwipe, straightShot, gashFromSwipe) belong to the wound recipes (GoreRecipeGoldenChecker) and are skipped here,
// as is cleanBlood (source-sprite clean-up, not part of the cut engine).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Laubrary.GoreLab.Tests
{
    public sealed class GoldenCaseResult
    {
        public string id, kind, status, detail;   // status: PASS | FAIL | SKIP
        public override string ToString() { return status + "  " + kind + "  " + id + (string.IsNullOrEmpty(detail) ? "" : "  -- " + detail); }
    }

    public static class GoreGoldenChecker
    {
        public const string FileName = "GORELAB_GOLDEN.json";
        static readonly string[] MemberNames = { "head", "torso" };

        public static List<GoldenCaseResult> RunAll(string jsonText, Func<string, bool> kindFilter = null)
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(jsonText);
            var sprites = (Dictionary<string, object>)root["sprites"];
            var style = GoreStyle.Fleshy();
            var results = new List<GoldenCaseResult>();
            foreach (var co in (List<object>)root["cases"])
            {
                var c = (Dictionary<string, object>)co;
                string kind = (string)c["kind"], id = (string)c["id"];
                if (kindFilter != null && !kindFilter(kind)) continue;
                var r = new GoldenCaseResult { id = id, kind = kind };
                var errs = new List<string>();
                try
                {
                    switch (kind)
                    {
                        case "primitives": CheckPrimitives(Obj(c["expected"]), errs); break;
                        case "normHead": CheckNormHead(c, errs); break;
                        case "mirrorHead": CheckMirrorHead(c, errs); break;
                        case "mirrorFrame": CheckMirrorFrame(c, sprites, errs); break;
                        case "makeSolid": CheckSolid(c, errs); break;
                        case "cutHead3D": CheckCutHead(c, sprites, style, errs); break;
                        case "cutFrame": CheckCutFrame(c, sprites, style, errs); break;
                        case "deathBake": CheckDeathBake(c, sprites, style, errs); break;
                        default: r.status = "SKIP"; r.detail = "not part of the cut engine"; break;
                    }
                }
                catch (Exception ex) { errs.Add("exception: " + ex); }
                if (r.status == null)
                {
                    r.status = errs.Count == 0 ? "PASS" : "FAIL";
                    r.detail = errs.Count == 0 ? "" : string.Join(" | ", errs.GetRange(0, Math.Min(6, errs.Count))) + (errs.Count > 6 ? $" | (+{errs.Count - 6} more)" : "");
                }
                results.Add(r);
            }
            return results;
        }

        // ------------------------------------------------------------------ primitives, tags, solids

        static void CheckPrimitives(Dictionary<string, object> e, List<string> errs)
        {
            foreach (var o in Arr(e["hash"]))
            {
                var h = Obj(o);
                double v = GoreRng.Hash(GoreRng.ToInt32(Num(h["x"])), GoreRng.ToInt32(Num(h["y"])), GoreRng.ToInt32(Num(h["s"])));
                if (Math.Round(v * 4294967296.0) != Num(h["uint32"])) errs.Add($"hash({Num(h["x"])},{Num(h["y"])},{Num(h["s"])}) = {v}");
            }
            foreach (var o in Arr(e["rng"]))
            {
                var h = Obj(o);
                var R = GoreRng.Rng(unchecked((int)(uint)Num(h["seedAsUint32"])));
                foreach (var d in Arr(h["first6"])) { double v = R(); if (Math.Round(v * 4294967296.0) != Num(Obj(d)["uint32"])) errs.Add($"rng seed {Num(h["seed"])}: {v}"); }
            }
            foreach (var o in Arr(e["vnoise3"]))
            {
                var h = Obj(o);
                double v = GoreRng.VNoise3(Num(h["x"]), Num(h["y"]), Num(h["z"]), GoreRng.ToInt32(Num(h["seed"])));
                if (Math.Abs(v - Num(h["value"])) > 1e-12) errs.Add($"vnoise3 {v} vs {Num(h["value"])}");
            }
            foreach (var o in Arr(e["vnoise1"]))
            {
                // prototype-only 1D noise (free Cut tab); checked here so every primitive is covered
                var h = Obj(o);
                double x = Num(h["x"]); int s = GoreRng.ToInt32(Num(h["seed"])); double fl = Math.Floor(x), f = x - fl, u = f * f * (3 - 2 * f);
                int i = GoreRng.ToInt32(fl);
                double v = GoreRng.Hash(i, 0, s) * (1 - u) + GoreRng.Hash(i + 1, 0, s) * u;
                if (Math.Abs(v - Num(h["value"])) > 1e-12) errs.Add($"vnoise1 {v} vs {Num(h["value"])}");
            }
            foreach (var o in Arr(e["mix"]))
            {
                var h = Obj(o);
                uint v = GoreColour.Mix((uint)Num(h["c1"]), (uint)Num(h["c2"]), Num(h["t"]));
                if (v != (uint)Num(h["result"])) errs.Add($"mix {v:x8} vs {(uint)Num(h["result"]):x8}");
            }
        }

        static void CheckNormHead(Dictionary<string, object> c, List<string> errs)
        {
            var ins = Arr(c["input"]); var exs = Arr(c["expected"]);
            for (int k = 0; k < ins.Count; k++)
            {
                var raw = Obj(ins[k]);
                MemberTag t = TagRaw(raw);
                GoreTagMath.Normalize(ref t);
                CompareTag("sample " + k, t, Obj(exs[k]), errs);
            }
        }

        static void CheckMirrorHead(Dictionary<string, object> c, List<string> errs)
        {
            var i = Obj(c["input"]); var e = Obj(c["expected"]); int w = (int)Num(i["w"]);
            foreach (var name in new[] { "head", "torso" })
                CompareTag(name, GoreTagMath.Mirror(Tag(Obj(i[name])), w), Obj(e[name]), errs);
        }

        static void CheckMirrorFrame(Dictionary<string, object> c, Dictionary<string, object> sprites, List<string> errs)
        {
            var i = Obj(c["input"]); var src = Obj(i["source"]); var e = Obj(c["expected"]);
            GoreGrid spr = Sprite(sprites, "imp24");
            int n = spr.w * spr.h;
            var members = new[]
            {
                new GoreMemberInput { present = true, tag = Tag(Obj(src["head"])), behind = Mask(src, "behind", n), exempt = Mask(src, "exempt", n) },
                new GoreMemberInput { present = true, tag = Tag(Obj(src["torso"])), behind = Mask(src, "tbehind", n), exempt = Mask(src, "texempt", n) },
            };
            GoreCut.MirrorFrame(spr, members, out GoreGrid mg, out GoreMemberInput[] mm);
            CompareTag("head", mm[0].tag, Obj(e["head"]), errs);
            CompareTag("torso", mm[1].tag, Obj(e["torso"]), errs);
            CompareMask("behind", mm[0].behind, e, errs); CompareMask("exempt", mm[0].exempt, e, errs);
            CompareMask("tbehind", mm[1].behind, e, errs); CompareMask("texempt", mm[1].exempt, e, errs);
            GoreGrid flipped = Sprite(sprites, "imp24_flipped");
            for (int k = 0; k < n; k++) if (mg.px[k] != flipped.px[k]) { errs.Add("mirrored pixels differ at " + k); break; }
        }

        static void CheckSolid(Dictionary<string, object> c, List<string> errs)
        {
            var i = Obj(c["input"]); var tags = Obj(i["tags"]); var Os = Arr(i["O"]); var e = Obj(c["expected"]);
            foreach (var kv in tags)
            {
                MemberTag t = Tag(Obj(kv.Value));
                var rows = Arr(e[kv.Key]);
                for (int k = 0; k < Os.Count; k++)
                {
                    var O = Arr(Os[k]); var ex = Obj(rows[k]); var pf = Arr(ex["Pf"]);
                    GoreSolid.Intersect(t, Num(O[0]), Num(O[1]), Num(O[2]), out double zf, out double zb, out double px, out double py, out double pz);
                    Near($"{kv.Key}[{k}].zf", zf, Num(ex["zf"]), errs); Near($"{kv.Key}[{k}].zb", zb, Num(ex["zb"]), errs);
                    Near($"{kv.Key}[{k}].Pf.x", px, Num(pf[0]), errs); Near($"{kv.Key}[{k}].Pf.y", py, Num(pf[1]), errs); Near($"{kv.Key}[{k}].Pf.z", pz, Num(pf[2]), errs);
                }
            }
        }

        // ------------------------------------------------------------------ cuts

        static void CheckCutHead(Dictionary<string, object> c, Dictionary<string, object> sprites, GoreStyle style, List<string> errs)
        {
            var i = Obj(c["input"]);
            var canvas = Obj(i["canvas"]); int W = (int)Num(canvas["W"]), H = (int)Num(canvas["H"]);
            var place = Arr(i["place"]); int sx = (int)Num(place[0]), sy = (int)Num(place[1]);
            GoreGrid full = Place(Sprite(sprites, (string)i["sprite"]), W, H, sx, sy);
            var ops = Ops(Arr(i["ops"]));
            int? lastG = i.ContainsKey("lastG") ? (int?)(int)Num(i["lastG"]) : null;
            var res = new GoreFrameResult();
            GoreCut.CutMember(full, sx, sy, Tag(Obj(i["tag"])), ops, Mask(i, "behindCanvas", W * H), Mask(i, "exemptCanvas", W * H), lastG, Cfg(Obj(i["cfg"])), style, res);
            CompareDescribe(res, full, W, H, Obj(c["expected"]), errs);
        }

        static void CheckCutFrame(Dictionary<string, object> c, Dictionary<string, object> sprites, GoreStyle style, List<string> errs)
        {
            var i = Obj(c["input"]);
            var canvas = Obj(i["canvas"]); int W = (int)Num(canvas["W"]), H = (int)Num(canvas["H"]);
            var place = Arr(i["place"]); int sx = (int)Num(place[0]), sy = (int)Num(place[1]);
            var fr = Obj(i["frame"]);
            GoreGrid spr = Sprite(sprites, (string)fr["sprite"]);
            GoreGrid full = Place(spr, W, H, sx, sy);
            var input = new GoreFrameInput { grid = full, sx = sx, sy = sy, members = FrameMembers(fr, spr.w, sx, sy, W, H) };
            var res = new GoreFrameResult();
            GoreCut.CutFrame(input, Ops(Arr(i["ops"])), Cfg(Obj(i["cfg"])), style, res);
            CompareDescribe(res, full, W, H, Obj(c["expected"]), errs);
        }

        static void CheckDeathBake(Dictionary<string, object> c, Dictionary<string, object> sprites, GoreStyle style, List<string> errs)
        {
            // The prototype's bake flow around the engine: place each frame, cut every frame from the hit frame on.
            var i = Obj(c["input"]); var e = Obj(c["expected"]);
            var canvas = Obj(i["canvas"]); int W = (int)Num(canvas["W"]), H = (int)Num(canvas["H"]), floorY = H - (int)Num(canvas["floorMargin"]);
            var ops = Ops(Arr(i["ops"])); var cfg = Cfg(Obj(i["cfg"])); int hit = (int)Num(i["hit"]);
            var frames = Arr(i["frames"]); var exFrames = Arr(e["frames"]);
            var bakeErrors = new List<string>();
            if (floorY != (int)Num(e["floorY"])) errs.Add("floorY");
            var res = new GoreFrameResult();
            for (int k = 0; k < frames.Count; k++)
            {
                var fr = Obj(frames[k]); var ex = Obj(exFrames[k]); string tagK = "frame " + k + ": ";
                GoreGrid spr = Sprite(sprites, (string)fr["sprite"]);
                int ox = fr.ContainsKey("ox") ? (int)Num(fr["ox"]) : 0, oy = fr.ContainsKey("oy") ? (int)Num(fr["oy"]) : 0;
                int sx = (int)Math.Floor((W - spr.w) / 2.0) + ox, sy = floorY - spr.h + oy;
                var pa = Arr(ex["placedAt"]);
                if (sx != (int)Num(pa[0]) || sy != (int)Num(pa[1])) errs.Add(tagK + "placedAt");
                bool baked = k >= hit && ops.Count > 0;
                if (k >= hit && ops.Count == 0) bakeErrors.Add($"Frame {k + 1} needs a cut: draw a slice in Play");
                if (baked != (bool)ex["hasHeadless"]) { errs.Add(tagK + "hasHeadless"); continue; }
                if (!baked) continue;
                GoreGrid full = Place(spr, W, H, sx, sy);
                GoreCut.CutFrame(new GoreFrameInput { grid = full, sx = sx, sy = sy, members = FrameMembers(fr, spr.w, sx, sy, W, H) }, ops, cfg, style, res);
                if (res.missing != (bool)ex["missing"]) errs.Add(tagK + "missing");
                if (res.changed != (int)Num(ex["changedPixels"])) errs.Add(tagK + $"changed {res.changed} vs {Num(ex["changedPixels"])}");
                if (Hex(Fnv(res.body)) != (string)ex["bodyHash"]) errs.Add(tagK + "bodyHash");
                CompareIntList(tagK + "partSizes", PartSizes(res), Arr(ex["partSizesSorted"]), errs);
                if (res.gibs.Count != (int)Num(ex["gibCount"])) errs.Add(tagK + "gibCount");
                if (res.bleed.Count != (int)Num(ex["stumpPointCount"])) errs.Add(tagK + "stumpPointCount");
                var wc = Arr(ex["woundCentroid"]);
                Near(tagK + "centroid.x", res.chunkCx, Num(wc[0]), errs); Near(tagK + "centroid.y", res.chunkCy, Num(wc[1]), errs);
                object neckO = ex["neck"];
                if (res.bleed.Count == 0) { if (neckO != null) errs.Add(tagK + "neck expected"); }
                else if (neckO == null) errs.Add(tagK + "neck not expected");
                else
                {
                    double x = 0, y = 0, nx = 0, ny = 0;
                    foreach (var p in res.bleed) { x += p.x + 0.5; y += p.y + 0.5; nx += p.nx; ny += p.ny; }
                    double l = Math.Sqrt(nx * nx + ny * ny); if (l == 0) l = 1;
                    var nk = Obj(neckO); int cnt = res.bleed.Count;
                    Near(tagK + "neck.x", x / cnt, Num(nk["x"]), errs); Near(tagK + "neck.y", y / cnt, Num(nk["y"]), errs);
                    Near(tagK + "neck.nx", nx / l, Num(nk["nx"]), errs); Near(tagK + "neck.ny", ny / l, Num(nk["ny"]), errs);
                }
            }
            var exErr = Arr(e["errors"]);
            if (exErr.Count != bakeErrors.Count) errs.Add($"errors {bakeErrors.Count} vs {exErr.Count}");
            else for (int k = 0; k < exErr.Count; k++) if ((string)exErr[k] != bakeErrors[k]) errs.Add("error text " + k);
        }

        /// <summary>The prototype's describe(): pixel states, chunk mask, counts, pieces, gibs, directions, bleed points and hashes.</summary>
        static void CompareDescribe(GoreFrameResult r, GoreGrid full, int W, int H, Dictionary<string, object> e, List<string> errs)
        {
            int changed = 0, emptied = 0, recol = 0;
            var rows = Arr(e["stateRows"]); var chunkRows = Arr(e["chunkRows"]);
            int rowErrors = 0;
            for (int y = 0; y < H; y++)
            {
                var s = new StringBuilder(W); var cs = new StringBuilder(W);
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; uint a = full.px[i], b = r.body[i]; char ch = '.';
                    if ((a >> 24) != 0) { if ((b >> 24) == 0) { ch = '2'; emptied++; changed++; } else if (a != b) { ch = '1'; recol++; changed++; } else ch = '0'; }
                    else if ((b >> 24) != 0) { ch = '3'; changed++; }
                    s.Append(ch); cs.Append(r.chunkMask[i] != 0 ? '#' : '.');
                }
                if (s.ToString() != (string)rows[y] && rowErrors++ < 3) errs.Add($"stateRow {y}: got {s} want {rows[y]}");
                if (cs.ToString() != (string)chunkRows[y] && rowErrors++ < 3) errs.Add($"chunkRow {y}: got {cs} want {chunkRows[y]}");
            }
            IntEq("changedPixels", changed, e, errs); IntEq("emptiedPixels", emptied, e, errs); IntEq("recolouredPixels", recol, e, errs);
            if (r.changed != changed) errs.Add($"result.changed {r.changed} vs {changed}");
            IntEq("chunkPixels", r.chunkCount, e, errs);
            var cc = Arr(e["chunkCentroid"]); Near("chunkCentroid.x", r.chunkCx, Num(cc[0]), errs); Near("chunkCentroid.y", r.chunkCy, Num(cc[1]), errs);
            CompareIntList("partSizesSorted", PartSizes(r), Arr(e["partSizesSorted"]), errs);
            var parts = Arr(e["parts"]);
            if (parts.Count != r.pieces.Count) errs.Add($"parts {r.pieces.Count} vs {parts.Count}");
            else for (int k = 0; k < parts.Count; k++)
            {
                var p = Obj(parts[k]); GorePiece q = r.pieces[k]; string t = "part " + k + " ";
                if (q.x != (int)Num(p["x"]) || q.y != (int)Num(p["y"]) || q.grid.w != (int)Num(p["w"]) || q.grid.h != (int)Num(p["h"])) errs.Add(t + $"box {q.x},{q.y} {q.grid.w}x{q.grid.h}");
                if (Hex(Fnv(q.grid.px)) != (string)p["gridHash"]) errs.Add(t + "gridHash");
                if (q.edge.Count != (int)Num(p["edgePoints"])) errs.Add(t + $"edgePoints {q.edge.Count} vs {Num(p["edgePoints"])}");
                var o = Arr(p["out"]); Near(t + "out.x", q.outX, Num(o[0]), errs); Near(t + "out.y", q.outY, Num(o[1]), errs);
            }
            var gibs = Arr(e["gibs"]);
            if (gibs.Count != r.gibs.Count) errs.Add($"gibs {r.gibs.Count} vs {gibs.Count}");
            else for (int k = 0; k < gibs.Count; k++)
            {
                var g = Arr(gibs[k]);
                if (r.gibs[k].x != Num(g[0]) || r.gibs[k].y != Num(g[1]) || r.gibs[k].colour != (uint)Num(g[2])) errs.Add("gib " + k);
            }
            var oa = Arr(e["outAtt"]); Near("outAtt.x", r.outX, Num(oa[0]), errs); Near("outAtt.y", r.outY, Num(oa[1]), errs);
            IntEq("attPointCount", r.bleed.Count, e, errs);
            var ap = new uint[r.bleed.Count * 4];
            for (int k = 0; k < r.bleed.Count; k++)
            {
                var p = r.bleed[k];
                ap[k * 4] = (uint)p.x; ap[k * 4 + 1] = (uint)p.y;
                ap[k * 4 + 2] = (uint)(JsRound(p.nx * 1e4) + 20000); ap[k * 4 + 3] = (uint)(JsRound(p.ny * 1e4) + 20000);
            }
            if (Hex(Fnv(ap)) != (string)e["attPointsHash"]) errs.Add("attPointsHash");
            if (Hex(Fnv(r.body)) != (string)e["bodyHash"]) errs.Add("bodyHash");
            if (r.missing != (bool)e["missing"]) errs.Add("missing");
        }

        // ------------------------------------------------------------------ input building

        static GoreMemberInput[] FrameMembers(Dictionary<string, object> fr, int spriteW, int sx, int sy, int W, int H)
        {
            bool noHead = fr.ContainsKey("noHead") && (bool)fr["noHead"];
            var m = new GoreMemberInput[2];
            m[0] = new GoreMemberInput { present = fr.ContainsKey("head") && fr["head"] != null, skip = noHead };
            if (m[0].present) m[0].tag = Tag(Obj(fr["head"]));
            m[0].behind = CanvasMask(fr, "behind", spriteW, sx, sy, W, H); m[0].exempt = CanvasMask(fr, "exempt", spriteW, sx, sy, W, H);
            m[1] = new GoreMemberInput { present = fr.ContainsKey("torso") && fr["torso"] != null };
            if (m[1].present) m[1].tag = Tag(Obj(fr["torso"]));
            m[1].behind = CanvasMask(fr, "tbehind", spriteW, sx, sy, W, H); m[1].exempt = CanvasMask(fr, "texempt", spriteW, sx, sy, W, H);
            return m;
        }

        static byte[] CanvasMask(Dictionary<string, object> fr, string key, int spriteW, int sx, int sy, int W, int H)
        {
            if (!fr.ContainsKey(key) || fr[key] == null) return null;
            var list = Arr(fr[key]); if (list.Count == 0) return null;
            var m = new byte[W * H];
            foreach (var o in list)
            {
                int i = (int)Num(o), x = i % spriteW + sx, y = i / spriteW + sy;
                if (x >= 0 && y >= 0 && x < W && y < H) m[y * W + x] = 1;
            }
            return m;
        }

        static byte[] Mask(Dictionary<string, object> d, string key, int n)
        {
            if (!d.ContainsKey(key) || d[key] == null) return null;
            var list = Arr(d[key]); if (list.Count == 0) return null;
            var m = new byte[n];
            foreach (var o in list) m[(int)Num(o)] = 1;
            return m;
        }

        static List<GoreRemover> Ops(List<object> ops)
        {
            var list = new List<GoreRemover>();
            foreach (var oo in ops)
            {
                var o = Obj(oo);
                int g = o.ContainsKey("g") ? (int)Num(o["g"]) : 0;
                int m = o.ContainsKey("m") ? Array.IndexOf(MemberNames, (string)o["m"]) : 0;
                string t = o.ContainsKey("t") ? (string)o["t"] : (o.ContainsKey("N") ? "plane" : "cap");
                if (t == "plane")
                {
                    var N = Arr(o["N"]);
                    list.Add(GoreRemovers.Plane(Num(N[0]), Num(N[1]), Num(N[2]), Num(o["d"]), g, m));
                }
                else
                {
                    var a = Arr(o["a"]); var b = Arr(o["b"]);
                    double bx = 0, by = 0;
                    if (o.ContainsKey("back") && o["back"] != null) { var bk = Arr(o["back"]); bx = Num(bk[0]); by = Num(bk[1]); }
                    list.Add(GoreRemovers.Capsule(Num(a[0]), Num(a[1]), Num(a[2]), Num(b[0]), Num(b[1]), Num(b[2]), Num(o["r"]), g, m, bx, by));
                }
            }
            return list;
        }

        static GoreCutConfig Cfg(Dictionary<string, object> c)
        {
            return new GoreCutConfig
            {
                seed = c.ContainsKey("seed") ? (int)Num(c["seed"]) : 0,
                jag = c.ContainsKey("jag") ? Num(c["jag"]) : 0,
                jagFreq = c.ContainsKey("jagFreq") ? Num(c["jagFreq"]) : 0,
                bone = c.ContainsKey("bone") && (bool)c["bone"],
            };
        }

        /// <summary>A tag as stored in the golden file (already normalised).</summary>
        public static MemberTag Tag(Dictionary<string, object> d)
        {
            var t = TagRaw(d);
            return t;
        }

        static MemberTag TagRaw(Dictionary<string, object> d)
        {
            var t = new MemberTag();
            t.kind = d.ContainsKey("kind") && (string)d["kind"] == "box" ? MemberKind.Box : MemberKind.Ball;
            var c = Arr(d["c"]); t.cx = Num(c[0]); t.cy = Num(c[1]);
            if (d.ContainsKey("rx")) { t.rx = Num(d["rx"]); t.ry = d.ContainsKey("ry") ? Num(d["ry"]) : t.rx; }
            else if (d.ContainsKey("r")) { t.rx = t.ry = Num(d["r"]); }
            if (d.ContainsKey("rz") && d["rz"] != null) t.rz = Num(d["rz"]);
            if (d.ContainsKey("n")) t.n = Num(d["n"]);
            if (d.ContainsKey("a")) t.angle = Num(d["a"]);
            if (d.ContainsKey("u3")) { var u = Arr(d["u3"]); t.ux = Num(u[0]); t.uy = Num(u[1]); t.uz = Num(u[2]); }
            if (d.ContainsKey("f3")) { var f = Arr(d["f3"]); t.fx = Num(f[0]); t.fy = Num(f[1]); t.fz = Num(f[2]); }
            return t;
        }

        static void CompareTag(string what, MemberTag t, Dictionary<string, object> e, List<string> errs)
        {
            var c = Arr(e["c"]);
            Near(what + ".c.x", t.cx, Num(c[0]), errs); Near(what + ".c.y", t.cy, Num(c[1]), errs);
            Near(what + ".rx", t.rx, Num(e["rx"]), errs); Near(what + ".ry", t.ry, Num(e["ry"]), errs);
            if (e.ContainsKey("rz") && e["rz"] != null) Near(what + ".rz", t.rz, Num(e["rz"]), errs);
            Near(what + ".n", t.n, Num(e["n"]), errs); Near(what + ".a", t.angle, Num(e["a"]), errs);
            var u = Arr(e["u3"]); var f = Arr(e["f3"]);
            Near(what + ".u.x", t.ux, Num(u[0]), errs); Near(what + ".u.y", t.uy, Num(u[1]), errs); Near(what + ".u.z", t.uz, Num(u[2]), errs);
            Near(what + ".f.x", t.fx, Num(f[0]), errs); Near(what + ".f.y", t.fy, Num(f[1]), errs); Near(what + ".f.z", t.fz, Num(f[2]), errs);
            bool box = e.ContainsKey("kind") && (string)e["kind"] == "box";
            if (box != (t.kind == MemberKind.Box)) errs.Add(what + ".kind");
        }

        static void CompareMask(string key, byte[] got, Dictionary<string, object> e, List<string> errs)
        {
            var want = new List<int>(); foreach (var o in Arr(e[key])) want.Add((int)Num(o));
            want.Sort();
            var have = new List<int>(); if (got != null) for (int i = 0; i < got.Length; i++) if (got[i] != 0) have.Add(i);
            if (want.Count != have.Count) { errs.Add($"{key}: {have.Count} vs {want.Count}"); return; }
            for (int k = 0; k < want.Count; k++) if (want[k] != have[k]) { errs.Add(key + " differs"); return; }
        }

        // ------------------------------------------------------------------ sprites and hashes

        internal static GoreGrid Sprite(Dictionary<string, object> sprites, string id)
        {
            var s = Obj(sprites[id]); int w = (int)Num(s["w"]), h = (int)Num(s["h"]);
            var g = new GoreGrid(w, h); int p = 0;
            foreach (var run in Arr(s["rle"])) { var r = Arr(run); int cnt = (int)Num(r[0]); uint v = (uint)Num(r[1]); for (int k = 0; k < cnt; k++) g.px[p++] = v; }
            return g;
        }

        internal static GoreGrid Place(GoreGrid spr, int W, int H, int sx, int sy)
        {
            var g = new GoreGrid(W, H);
            for (int y = 0; y < spr.h; y++)
                for (int x = 0; x < spr.w; x++)
                {
                    int tx = x + sx, ty = y + sy;
                    if (tx >= 0 && ty >= 0 && tx < W && ty < H) g.px[ty * W + tx] = spr.px[y * spr.w + x];
                }
            return g;
        }

        public static uint Fnv(uint[] a)
        {
            uint h = 0x811c9dc5;
            unchecked
            {
                for (int i = 0; i < a.Length; i++) { uint v = a[i]; for (int k = 0; k < 4; k++) { h ^= v & 255; h *= 0x01000193; v >>= 8; } }
            }
            return h;
        }

        static string Hex(uint v) { return v.ToString("x8"); }
        static double JsRound(double v) { return Math.Floor(v + 0.5); }

        static List<int> PartSizes(GoreFrameResult r)
        {
            var l = new List<int>();
            foreach (var p in r.pieces) { int c = 0; foreach (var v in p.grid.px) if ((v >> 24) != 0) c++; l.Add(c); }
            l.Sort((a, b) => b.CompareTo(a));
            return l;
        }

        static void CompareIntList(string what, List<int> got, List<object> want, List<string> errs)
        {
            if (want == null) return;
            if (got.Count != want.Count) { errs.Add($"{what}: [{string.Join(",", got)}] vs {want.Count} entries"); return; }
            for (int k = 0; k < got.Count; k++) if (got[k] != (int)Num(want[k])) { errs.Add($"{what}: [{string.Join(",", got)}]"); return; }
        }

        static void IntEq(string key, int got, Dictionary<string, object> e, List<string> errs)
        {
            if (got != (int)Num(e[key])) errs.Add($"{key} {got} vs {Num(e[key])}");
        }

        static void Near(string what, double got, double want, List<string> errs)
        {
            if (Math.Abs(got - want) > 1e-6 * Math.Max(1, Math.Abs(want))) errs.Add($"{what} {got.ToString("R", CultureInfo.InvariantCulture)} vs {want.ToString("R", CultureInfo.InvariantCulture)}");
        }

        static Dictionary<string, object> Obj(object o) { return (Dictionary<string, object>)o; }
        static List<object> Arr(object o) { return (List<object>)o; }
        static double Num(object o) { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
    }

    /// <summary>Minimal JSON reader: objects, arrays, numbers (double), strings, true/false/null.</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; object v = Value(s, ref i); return v; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i++] == '}') return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i++] == ']') return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
    }
}
