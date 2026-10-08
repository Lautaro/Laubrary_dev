// The cut: every pixel of a member looks straight into the screen, finds where its line of sight passes through the member's solid, lets
// each remover take its stretch out of that line, and shows what is left: the original pixel, a freshly exposed wound surface, or nothing.
// The pixels the newest group changed become the flying chunk. Everything is evaluated from the intact sprite, so no state is accumulated.
//
// Performance shape: all per-pixel work runs over flat arrays held in a reusable scratch object, with no allocation per pixel, so one
// frame's evaluation can later be moved into a Burst job unchanged. Only the pieces handed to the caller are allocated (one per piece).
using System;
using System.Collections.Generic;

namespace Laubrary.GoreLab
{
    public static class GoreCut
    {
        const double Eps = 1e-9;

        [ThreadStatic] static Scratch s_scratch;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Cut one frame with every remover, member by member, and merge the members' results. Where members overlap on screen the earlier
        /// member in the list owns the pixel (with the default list Head, Torso: the head). The newest group (highest group number over all
        /// removers) is the one whose changed pixels become the chunk, pieces and gibs. If a member that has removers has no tag on this frame
        /// (and is not flagged skip) the frame is "not set up": the result is the intact frame with missing = true.
        /// </summary>
        public static void CutFrame(in GoreFrameInput frame, IReadOnlyList<GoreRemover> removers, in GoreCutConfig cfg, GoreStyle style, GoreFrameResult result)
        {
            GoreGrid grid = frame.grid;
            int n = grid.w * grid.h;
            var s = Scratch.Get(n);
            Reset(result, grid);

            GoreMemberInput[] members = frame.members ?? Array.Empty<GoreMemberInput>();
            int lastG = 0;
            int removerCount = removers == null ? 0 : removers.Count;
            for (int i = 0; i < removerCount; i++) if (i == 0 || removers[i].group > lastG) lastG = removers[i].group;

            // which members have removers; any of them untagged (and not deliberately skipped) means the frame is not set up
            int mc = members.Length;
            for (int i = 0; i < removerCount; i++)
            {
                int m = removers[i].member;
                if (m < 0 || m >= mc || (!members[m].present && !members[m].skip)) { result.missing = true; return; }
            }

            Array.Clear(s.owned, 0, n);
            bool anyOwned = false;
            double cxSum = 0, cySum = 0; int best = 0;
            for (int m = 0; m < mc; m++)
            {
                ref GoreMemberInput mi = ref members[m];
                bool involved = false;
                for (int i = 0; i < removerCount; i++) if (removers[i].member == m) { involved = true; break; }

                if (involved && mi.present && !mi.skip)
                {
                    byte[] exempt = mi.exempt;
                    if (anyOwned)
                    {
                        for (int i = 0; i < n; i++) s.exemptMerged[i] = (byte)((exempt != null && exempt[i] != 0) || s.owned[i] != 0 ? 1 : 0);
                        exempt = s.exemptMerged;
                    }
                    GoreFrameResult r = s.memberResult;
                    Reset(r, grid);
                    CutMemberCore(s, grid, frame.sx, frame.sy, mi.tag, removers, m, mi.behind, exempt, lastG, cfg, style, r);

                    uint[] src = grid.px;
                    for (int i = 0; i < n; i++)
                    {
                        if (r.body[i] != src[i]) result.body[i] = r.body[i];
                        if (r.chunkMask[i] != 0) result.chunkMask[i] = 1;
                    }
                    result.bleed.AddRange(r.bleed);
                    result.pieces.AddRange(r.pieces);
                    result.gibs.AddRange(r.gibs);
                    result.chunkCount += r.chunkCount;
                    cxSum += r.chunkCx * r.chunkCount; cySum += r.chunkCy * r.chunkCount;
                    if (r.chunkCount > best) { best = r.chunkCount; result.outX = r.outX; result.outY = r.outY; }
                    r.pieces.Clear();
                }

                // a present, non-skipped member owns its outline's pixels against the members after it
                if (mi.present && !mi.skip && m + 1 < mc && LaterMemberInvolved(removers, removerCount, m))
                {
                    MarkOutline(grid, frame.sx, frame.sy, mi.tag, mi.exempt, s.owned);
                    anyOwned = true;
                }
            }

            SortPieces(result.pieces);
            if (result.chunkCount > 0) { result.chunkCx = cxSum / result.chunkCount; result.chunkCy = cySum / result.chunkCount; }
            result.changed = CountChanged(grid.px, result.body);
        }

        /// <summary>
        /// Cut a single member: every remover in the list is applied to this member's tag regardless of its member field.
        /// newestGroup = the group treated as newest (CutFrame passes the highest group over ALL members); null = the highest in this list.
        /// behind / exempt are per grid pixel (or null). The chunk centroid of an empty chunk is the member centre.
        /// </summary>
        public static void CutMember(GoreGrid grid, int sx, int sy, in MemberTag tag, IReadOnlyList<GoreRemover> removers, byte[] behind, byte[] exempt,
                                     int? newestGroup, in GoreCutConfig cfg, GoreStyle style, GoreFrameResult result)
        {
            var s = Scratch.Get(grid.w * grid.h);
            Reset(result, grid);
            int lastG = 0;
            if (newestGroup.HasValue) lastG = newestGroup.Value;
            else for (int i = 0; i < removers.Count; i++) if (i == 0 || removers[i].group > lastG) lastG = removers[i].group;
            CutMemberCore(s, grid, sx, sy, tag, removers, -1, behind, exempt, lastG, cfg, style, result);
            result.changed = CountChanged(grid.px, result.body);
        }

        /// <summary>
        /// The horizontally mirrored view of a frame (for a renderer flipX): pixels, tags and masks mirrored about the grid's width. The grid is the
        /// sprite itself (sprite offset 0). Removers are NOT mirrored: a mirrored view is the same body turned around.
        /// </summary>
        public static void MirrorFrame(GoreGrid grid, GoreMemberInput[] members, out GoreGrid mirroredGrid, out GoreMemberInput[] mirroredMembers)
        {
            int w = grid.w, h = grid.h;
            mirroredGrid = new GoreGrid(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mirroredGrid.px[y * w + (w - 1 - x)] = grid.px[y * w + x];

            mirroredMembers = new GoreMemberInput[members == null ? 0 : members.Length];
            for (int m = 0; m < mirroredMembers.Length; m++)
            {
                GoreMemberInput src = members[m];
                mirroredMembers[m] = new GoreMemberInput
                {
                    present = src.present,
                    skip = src.skip,
                    tag = GoreTagMath.Mirror(src.tag, w),
                    behind = MirrorMask(src.behind, w, h),
                    exempt = MirrorMask(src.exempt, w, h),
                };
            }
        }

        static byte[] MirrorMask(byte[] mask, int w, int h)
        {
            if (mask == null) return null;
            var o = new byte[mask.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    o[y * w + (w - 1 - x)] = mask[y * w + x];
            return o;
        }

        // ------------------------------------------------------------------ one member

        // member >= 0: only removers of that member are used (their position in that filtered list is their noise index); -1: all of them.
        static void CutMemberCore(Scratch s, GoreGrid grid, int sx, int sy, in MemberTag tag, IReadOnlyList<GoreRemover> removers, int member,
                                  byte[] behind, byte[] exempt, int lastG, in GoreCutConfig cfg, GoreStyle style, GoreFrameResult res)
        {
            int W = grid.w, H = grid.h, n = W * H;
            uint[] full = grid.px, body = res.body;

            // ---- per-cut set-up: removers, member axes, solid ----
            int count = 0;
            for (int i = 0; i < removers.Count; i++)
            {
                if (member >= 0 && removers[i].member != member) continue;
                s.EnsureRemovers(count + 1);
                s.ops[count] = PreparedRemover.Of(removers[i], count);
                count++;
            }
            bool hasOld = false;
            for (int k = 0; k < count; k++) if (s.ops[k].group != lastG) { hasOld = true; break; }

            var e = GoreTagMath.East(tag);
            double rx = tag.rx, ry = tag.ry, rz = GoreTagMath.Depth(tag);
            double sEx = e.x / rx, sEy = e.y / rx, sEz = e.z / rx;
            double sUx = tag.ux / ry, sUy = tag.uy / ry, sUz = tag.uz / ry;
            double sFx = tag.fx / rz, sFy = tag.fy / rz, sFz = tag.fz / rz;
            double cx = tag.cx + sx, cy = tag.cy + sy;
            var solid = GoreSolidShape.Of(tag);
            var env = new RemoverEnv
            {
                dvx = sEz, dvy = sUz, dvz = sFz,
                qa = sEz * sEz + sUz * sUz + sFz * sFz,
                ja = cfg.jag / 6, jf = (cfg.jagFreq == 0 ? 0.45 : cfg.jagFreq) * 6,
                seed = unchecked(cfg.seed * 7 + 3),
                bone = cfg.bone,
            };
            double ucos = Math.Cos(tag.angle), usin = Math.Sin(tag.angle);

            int[] key = s.key; byte[] stB = s.stateB, stA = s.stateA; int[] owB = s.ownerB, owA = s.ownerA; uint[] colB = s.colB, colA = s.colA;
            Array.Clear(key, 0, n); Array.Clear(stB, 0, n); Array.Clear(stA, 0, n);

            // ---- per pixel: the line of sight through the solid, and what it shows with all removers and without the newest group ----
            double R = Math.Max(rx, ry) * 1.7;
            int x0 = Math.Max(0, (int)Math.Floor(cx - R)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx + R));
            int y0 = Math.Max(0, (int)Math.Floor(cy - R)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy + R));
            int pc = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    if ((full[i] >> 24) == 0 || (exempt != null && exempt[i] != 0)) continue;
                    if (!GoreTagMath.InsideOutline(tag.cx, tag.cy, rx, ry, tag.n, ucos, usin, x + 0.5 - sx, y + 0.5 - sy)) continue;

                    double X = x + 0.5 - cx, Y = y + 0.5 - cy;
                    double ox = sEx * X + sEy * Y, oy = sUx * X + sUy * Y, oz = sFx * X + sFy * Y;
                    GoreSolid.Intersect(in solid, ox, oy, oz, out double zf, out double zb, out double pfx, out double pfy, out double pfz);
                    key[i] = GoreRng.ToInt32((Math.Floor(pfx * 9) + 40) * 10007 + (Math.Floor(pfy * 9) + 40) * 101 + Math.Floor(pfz * 9) + 40);
                    s.pxIndex[pc] = i; s.pxO[pc * 3] = ox; s.pxO[pc * 3 + 1] = oy; s.pxO[pc * 3 + 2] = oz;
                    pc++;

                    EvaluatePixel(s.ops, count, in env, ox, oy, oz, s.ivLo, s.ivHi, s.ivHas);
                    Walk(s.ops, count, s.ivLo, s.ivHi, s.ivHas, zf, zb, false, lastG, out byte st, out int own, out double z);
                    stB[i] = st; owB[i] = own;
                    if (st == 1) colB[i] = RemoverEval.Colour(ref s.ops[own], in env, style, ox + env.dvx * z, oy + env.dvy * z, oz + env.dvz * z);
                    if (hasOld)
                    {
                        Walk(s.ops, count, s.ivLo, s.ivHi, s.ivHas, zf, zb, true, lastG, out st, out own, out z);
                        stA[i] = st; owA[i] = own;
                        if (st == 1) colA[i] = RemoverEval.Colour(ref s.ops[own], in env, style, ox + env.dvx * z, oy + env.dvy * z, oz + env.dvz * z);
                    }
                }

            // ---- the chunk: pixels the newest group changed (judged against what the older groups left) ----
            byte[] chunk = res.chunkMask;
            int capCount = 0; double ccx = 0, ccy = 0;
            for (int p = 0; p < pc; p++)
            {
                int i = s.pxIndex[p];
                if ((Before(i, hasOld, stA, colA, full) >> 24) == 0) continue;
                if (stB[i] == 2 || (stB[i] == 1 && (!hasOld || stA[i] != 1 || owA[i] != owB[i])))
                {
                    chunk[i] = 1; capCount++;
                    ccx += (i % W) + 0.5; ccy += (i / W) + 0.5;
                }
            }
            double capX = capCount > 0 ? ccx / capCount : cx, capY = capCount > 0 ? ccy / capCount : cy;

            // ---- screen directions: where each remover's chunk leaves and where blood leaves its wound ----
            Unit2(capX - cx, capY - cy, 0, -1, out double cdx, out double cdy);
            for (int k = 0; k < count; k++)
            {
                ref PreparedRemover o = ref s.ops[k];
                ref readonly GoreRemover r = ref o.src;
                if (o.kind == RemoverKinds.Plane)
                {
                    double gx = r.nx * sEx + r.ny * sUx + r.nz * sFx, gy = r.nx * sEy + r.ny * sUy + r.nz * sFy, gz = r.nx * sEz + r.ny * sUz + r.nz * sFz;
                    double gl = JsMath.Hypot(gx, gy, gz); if (gl == 0) gl = 1;
                    if (JsMath.Hypot(gx, gy) / gl > 0.25) Unit2(gx, gy, 0, -1, out o.chunkX, out o.chunkY);
                    else if (o.group == lastG) { o.chunkX = cdx; o.chunkY = cdy; }
                    else { o.chunkX = 0; o.chunkY = -1; }
                    o.outX = o.chunkX; o.outY = o.chunkY;
                }
                else if (r.backX != 0 || r.backY != 0)
                {
                    Unit2(r.backX, r.backY, -1, 0, out o.chunkX, out o.chunkY);
                    o.outX = o.chunkX; o.outY = o.chunkY;
                }
                else if (o.kind == RemoverKinds.Capsule)
                {
                    double vx = r.bx - r.ax, vy = r.by - r.ay, vz = r.bz - r.az;
                    double scx = vx * e.x * rx + vy * tag.ux * ry + vz * tag.fx * rz, scy = vx * e.y * rx + vy * tag.uy * ry + vz * tag.fy * rz;
                    Unit2(scx, scy, 1, 0, out o.chunkX, out o.chunkY);
                    o.outX = -o.chunkX; o.outY = -o.chunkY;
                }
                else { o.chunkX = 0; o.chunkY = -1; o.outX = 0; o.outY = -1; }
            }
            double outAttX = 0, outAttY = -1;
            for (int k = 0; k < count; k++) if (s.ops[k].group == lastG) { outAttX = s.ops[k].chunkX; outAttY = s.ops[k].chunkY; break; }

            // ---- body: cut surfaces take their colour, emptied pixels vanish (or turn to dark gore where the body is behind) ----
            for (int p = 0; p < pc; p++)
            {
                int i = s.pxIndex[p];
                if (stB[i] == 1) body[i] = colB[i];
                else if (stB[i] == 2)
                {
                    body[i] = 0;
                    if (behind != null && behind[i] != 0)
                        body[i] = GoreColour.Mix(full[i], style.goreDark, 0.72 + 0.2 * GoreRng.Hash(key[i] & 0xffff, key[i] >> 16, 5));
                }
            }

            // ---- bleed points: every exposed surface, then the raw rim of the body beside an emptied part (recoloured as wound) ----
            byte[] seen = s.seen;
            Array.Clear(seen, 0, n);
            for (int p = 0; p < pc; p++)
            {
                int i = s.pxIndex[p];
                if (stB[i] != 1) continue;
                AddBleed(res.bleed, seen, i, W, s.ops[owB[i]].outX, s.ops[owB[i]].outY);
            }
            uint[] flesh = style.flesh;
            for (int i = 0; i < n; i++)
            {
                if (stB[i] != 0 || (body[i] >> 24) == 0 || (exempt != null && exempt[i] != 0)) continue;
                int x = i % W, y = i / W, ex = 0, ey = 0, cnt = 0;
                if (x + 1 < W && stB[i + 1] == 2 && (body[i + 1] >> 24) == 0) { ex += 1; cnt++; }
                if (x > 0 && stB[i - 1] == 2 && (body[i - 1] >> 24) == 0) { ex -= 1; cnt++; }
                if (y + 1 < H && stB[i + W] == 2 && (body[i + W] >> 24) == 0) { ey += 1; cnt++; }
                if (y > 0 && stB[i - W] == 2 && (body[i - W] >> 24) == 0) { ey -= 1; cnt++; }
                if (cnt == 0) continue;
                double l = JsMath.Hypot(ex, ey); if (l == 0) l = 1;
                body[i] = flesh[(int)(GoreRng.Hash(key[i] & 0xffff, key[i] >> 16, 12) * 3.2)];
                AddBleed(res.bleed, seen, i, W, ex / l, ey / l);
            }

            res.chunkCount = capCount;
            res.chunkCx = capX; res.chunkCy = capY;
            res.outX = outAttX; res.outY = outAttY;
            if (capCount == 0) return;

            // ---- the flying chunk: the "before" colours of the chunk pixels, the edge toward the body recoloured as wound ----
            uint[] cap = s.cap;
            Array.Clear(cap, 0, n);
            for (int i = 0; i < n; i++) if (chunk[i] != 0) cap[i] = Before(i, hasOld, stA, colA, full);
            for (int i = 0; i < n; i++)
            {
                if (chunk[i] == 0) continue;
                if (TornEdge(i, W, H, chunk, hasOld, stA, colA, full, exempt, out _, out _) > 0)
                    cap[i] = flesh[(int)(GoreRng.Hash(key[i] & 0xffff, key[i] >> 16, 13) * 3.2)];
            }

            // ---- connected pieces (4-connected, numbered in scan order): 3+ pixels fly as a piece, smaller ones as loose gibs ----
            int comps = Label(s, cap, W, H);
            for (int c = 0; c < comps; c++)
            {
                int bx0 = s.compX0[c], by0 = s.compY0[c], bx1 = s.compX1[c], by1 = s.compY1[c];
                if (s.compArea[c] < 3)
                {
                    for (int y = by0; y <= by1; y++)
                        for (int x = bx0; x <= bx1; x++)
                            if (s.label[y * W + x] == c) res.gibs.Add(new GoreGib { x = x + 0.5f, y = y + 0.5f, colour = cap[y * W + x] });
                    continue;
                }
                var piece = new GorePiece { grid = new GoreGrid(bx1 - bx0 + 1, by1 - by0 + 1), x = bx0, y = by0, outX = outAttX, outY = outAttY };
                int pw = piece.grid.w;
                for (int y = by0; y <= by1; y++)
                    for (int x = bx0; x <= bx1; x++)
                    {
                        int i = y * W + x;
                        if (s.label[i] != c) continue;
                        piece.grid.px[(y - by0) * pw + (x - bx0)] = cap[i];
                        if (TornEdge(i, W, H, chunk, hasOld, stA, colA, full, exempt, out int ax, out int ay) > 0)
                        {
                            double l = JsMath.Hypot(ax, ay); if (l == 0) l = 1;
                            piece.edge.Add(new GoreBleedPoint { x = x - bx0, y = y - by0, nx = (float)(ax / l), ny = (float)(ay / l) });
                        }
                    }
                res.pieces.Add(piece);
            }
            SortPieces(res.pieces);
        }

        // ------------------------------------------------------------------ per-pixel evaluation (static, plain data)

        /// <summary>Each remover's removed interval along this pixel's line of sight.</summary>
        static void EvaluatePixel(PreparedRemover[] ops, int count, in RemoverEnv env, double ox, double oy, double oz, double[] lo, double[] hi, bool[] has)
        {
            for (int k = 0; k < count; k++) has[k] = RemoverEval.Interval(ref ops[k], in env, ox, oy, oz, out lo[k], out hi[k]);
        }

        /// <summary>
        /// Walk from the front surface into the member: while some interval swallows the current front, jump to the start of the one that
        /// starts nearest the back. State 0 = untouched, 1 = cut surface owned by the last remover, 2 = the whole line of sight is gone.
        /// skipNewest leaves out the newest group (what the pixel showed before it).
        /// </summary>
        static void Walk(PreparedRemover[] ops, int count, double[] lo, double[] hi, bool[] has, double zf, double zb, bool skipNewest, int lastG,
                         out byte state, out int owner, out double z)
        {
            z = zf; owner = -1;
            for (int it = 0; it <= count; it++)
            {
                double bl = double.PositiveInfinity; int bk = -1;
                for (int k = 0; k < count; k++)
                {
                    if (!has[k] || (skipNewest && ops[k].group == lastG)) continue;
                    if (lo[k] < z - Eps && hi[k] >= z - Eps && lo[k] < bl) { bl = lo[k]; bk = k; }
                }
                if (bk < 0) break;
                z = bl; owner = bk;
                if (z <= zb + Eps) break;
            }
            if (owner < 0) state = 0;
            else state = z <= zb + Eps ? (byte)2 : (byte)1;
        }

        /// <summary>What a pixel showed before the newest group: emptied, an older wound surface, or the original.</summary>
        static uint Before(int i, bool hasOld, byte[] stA, uint[] colA, uint[] full)
        {
            if (!hasOld) return full[i];
            return stA[i] == 2 ? 0u : stA[i] == 1 ? colA[i] : full[i];
        }

        /// <summary>Number of 4-neighbours of a chunk pixel that stay on the body (solid before, not exempt, not chunk), and the sum of their offsets.</summary>
        static int TornEdge(int i, int W, int H, byte[] chunk, bool hasOld, byte[] stA, uint[] colA, uint[] full, byte[] exempt, out int ax, out int ay)
        {
            int x = i % W, y = i / W, k = 0; ax = 0; ay = 0;
            if (x + 1 < W && StaysOnBody(i + 1, chunk, hasOld, stA, colA, full, exempt)) { ax += 1; k++; }
            if (x > 0 && StaysOnBody(i - 1, chunk, hasOld, stA, colA, full, exempt)) { ax -= 1; k++; }
            if (y + 1 < H && StaysOnBody(i + W, chunk, hasOld, stA, colA, full, exempt)) { ay += 1; k++; }
            if (y > 0 && StaysOnBody(i - W, chunk, hasOld, stA, colA, full, exempt)) { ay -= 1; k++; }
            return k;
        }

        static bool StaysOnBody(int j, byte[] chunk, bool hasOld, byte[] stA, uint[] colA, uint[] full, byte[] exempt)
        {
            return chunk[j] == 0 && (Before(j, hasOld, stA, colA, full) >> 24) != 0 && !(exempt != null && exempt[j] != 0);
        }

        static void AddBleed(List<GoreBleedPoint> list, byte[] seen, int i, int W, double nx, double ny)
        {
            if (seen[i] != 0) return;
            seen[i] = 1;
            list.Add(new GoreBleedPoint { x = i % W, y = i / W, nx = (float)nx, ny = (float)ny });
        }

        static void Unit2(double x, double y, double fbx, double fby, out double ux, out double uy)
        {
            double l = JsMath.Hypot(x, y);
            if (l > 1e-3) { ux = x / l; uy = y / l; } else { ux = fbx; uy = fby; }
        }

        /// <summary>4-connected components of the solid pixels of grid, numbered in order of their first pixel in scan order, with areas and bounding boxes.</summary>
        static int Label(Scratch s, uint[] grid, int W, int H)
        {
            int n = W * H, comps = 0;
            int[] label = s.label, stack = s.stack;
            for (int i = 0; i < n; i++) label[i] = -1;
            for (int start = 0; start < n; start++)
            {
                if (label[start] >= 0 || (grid[start] >> 24) == 0) continue;
                s.EnsureComponents(comps + 1);
                int area = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, sp = 0;
                label[start] = comps; stack[sp++] = start;
                while (sp > 0)
                {
                    int i = stack[--sp], x = i % W, y = i / W;
                    area++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (x + 1 < W) Push(i + 1, comps, grid, label, stack, ref sp);
                    if (x > 0) Push(i - 1, comps, grid, label, stack, ref sp);
                    if (y + 1 < H) Push(i + W, comps, grid, label, stack, ref sp);
                    if (y > 0) Push(i - W, comps, grid, label, stack, ref sp);
                }
                s.compArea[comps] = area; s.compX0[comps] = minX; s.compY0[comps] = minY; s.compX1[comps] = maxX; s.compY1[comps] = maxY;
                comps++;
            }
            return comps;
        }

        static void Push(int j, int c, uint[] grid, int[] label, int[] stack, ref int sp)
        {
            if (label[j] >= 0 || (grid[j] >> 24) == 0) return;
            label[j] = c; stack[sp++] = j;
        }

        // ------------------------------------------------------------------ frame helpers

        static bool LaterMemberInvolved(IReadOnlyList<GoreRemover> removers, int count, int m)
        {
            for (int i = 0; i < count; i++) if (removers[i].member > m) return true;
            return false;
        }

        /// <summary>Mark the solid, non-exempt pixels inside a member's outline (the pixels it owns against later members).</summary>
        static void MarkOutline(GoreGrid grid, int sx, int sy, in MemberTag t, byte[] exempt, byte[] owned)
        {
            int W = grid.w, H = grid.h;
            double R = Math.Max(t.rx, t.ry) * 1.7, ucos = Math.Cos(t.angle), usin = Math.Sin(t.angle);
            int y0 = Math.Max(0, (int)Math.Floor(t.cy + sy - R)), y1 = Math.Min(H - 1, (int)Math.Ceiling(t.cy + sy + R));
            int x0 = Math.Max(0, (int)Math.Floor(t.cx + sx - R)), x1 = Math.Min(W - 1, (int)Math.Ceiling(t.cx + sx + R));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    if ((grid.px[i] >> 24) != 0 && !(exempt != null && exempt[i] != 0) && GoreTagMath.InsideOutline(t.cx, t.cy, t.rx, t.ry, t.n, ucos, usin, x + 0.5 - sx, y + 0.5 - sy))
                        owned[i] = 1;
                }
        }

        static void Reset(GoreFrameResult r, GoreGrid grid)
        {
            int n = grid.w * grid.h;
            if (r.body == null || r.body.Length != n) r.body = new uint[n];
            if (r.chunkMask == null || r.chunkMask.Length != n) r.chunkMask = new byte[n];
            Array.Copy(grid.px, r.body, n);
            Array.Clear(r.chunkMask, 0, n);
            r.changed = 0; r.chunkCount = 0; r.chunkCx = 0; r.chunkCy = 0; r.outX = 0; r.outY = -1; r.missing = false;
            r.bleed.Clear(); r.pieces.Clear(); r.gibs.Clear();
        }

        static int CountChanged(uint[] a, uint[] b)
        {
            int c = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) c++;
            return c;
        }

        /// <summary>Largest bounding box first; stable, so equal boxes keep their order.</summary>
        static void SortPieces(List<GorePiece> pieces)
        {
            for (int i = 1; i < pieces.Count; i++)
            {
                GorePiece p = pieces[i];
                int area = p.grid.w * p.grid.h, j = i - 1;
                while (j >= 0 && pieces[j].grid.w * pieces[j].grid.h < area) { pieces[j + 1] = pieces[j]; j--; }
                pieces[j + 1] = p;
            }
        }

        // ------------------------------------------------------------------ reusable buffers

        sealed class Scratch
        {
            public int n = -1;
            public int[] key, ownerB, ownerA, label, stack, pxIndex;
            public byte[] stateB, stateA, seen, owned, exemptMerged;
            public uint[] colB, colA, cap;
            public double[] pxO;
            public PreparedRemover[] ops = new PreparedRemover[16];
            public double[] ivLo = new double[16], ivHi = new double[16];
            public bool[] ivHas = new bool[16];
            public int[] compArea = new int[16], compX0 = new int[16], compY0 = new int[16], compX1 = new int[16], compY1 = new int[16];
            public readonly GoreFrameResult memberResult = new GoreFrameResult();

            public static Scratch Get(int n)
            {
                var s = s_scratch ?? (s_scratch = new Scratch());
                if (s.n < n)
                {
                    s.n = n;
                    s.key = new int[n]; s.ownerB = new int[n]; s.ownerA = new int[n]; s.label = new int[n]; s.stack = new int[n]; s.pxIndex = new int[n];
                    s.stateB = new byte[n]; s.stateA = new byte[n]; s.seen = new byte[n]; s.owned = new byte[n]; s.exemptMerged = new byte[n];
                    s.colB = new uint[n]; s.colA = new uint[n]; s.cap = new uint[n];
                    s.pxO = new double[n * 3];
                }
                return s;
            }

            public void EnsureRemovers(int c)
            {
                if (c <= ops.Length) return;
                int size = Math.Max(c, ops.Length * 2);
                Array.Resize(ref ops, size); Array.Resize(ref ivLo, size); Array.Resize(ref ivHi, size); Array.Resize(ref ivHas, size);
            }

            public void EnsureComponents(int c)
            {
                if (c <= compArea.Length) return;
                int size = Math.Max(c, compArea.Length * 2);
                Array.Resize(ref compArea, size); Array.Resize(ref compX0, size); Array.Resize(ref compY0, size); Array.Resize(ref compX1, size); Array.Resize(ref compY1, size);
            }
        }
    }
}
